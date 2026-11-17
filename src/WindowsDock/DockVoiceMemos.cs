using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace WindowsDock;

internal sealed record VoiceMemo(Guid Id, string Title, DateTimeOffset Recorded, TimeSpan Duration, string? Transcript = null);

internal static class DockVoiceMemos
{
    private const string State = "voice-memos.json";
    internal const int FileLimit = 24_000_000;
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromMinutes(2),
        MaxResponseContentBufferSize = 512 * 1024
    };

    internal static VoiceMemo[] Load() => DockData.Read<VoiceMemo[]>(State) ?? [];

    internal static string PathFor(Guid id, bool pending = false) =>
        Path.Combine(DockData.FilePath("VoiceMemos"), id.ToString("N") + (pending ? ".partial" : ".wav"));

    internal static VoiceMemo Commit(Guid id, string title)
    {
        var staged = PathFor(id, pending: true);
        var duration = Duration(staged);
        var memo = new VoiceMemo(id, CleanTitle(title), DateTimeOffset.Now, duration);
        File.Move(staged, PathFor(id));
        DockData.Update<VoiceMemo[]>(State, all => (all ?? []).Prepend(memo).ToArray());
        return memo;
    }

    internal static VoiceMemo Import(string source)
    {
        Duration(source);
        var id = Guid.NewGuid();
        var target = PathFor(id, pending: true);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        try
        {
            File.Copy(source, target);
            return Commit(id, Path.GetFileNameWithoutExtension(source));
        }
        finally
        {
            if (File.Exists(target))
                File.Delete(target);
        }
    }

    internal static void Edit(Guid id, string title, string? transcript) =>
        DockData.Update<VoiceMemo[]>(State, all => (all ?? []).Select(memo => memo.Id == id
            ? memo with { Title = CleanTitle(title), Transcript = transcript } : memo).ToArray());

    internal static void Remove(Guid id)
    {
        File.Delete(PathFor(id));
        DockData.Update<VoiceMemo[]>(State, all => (all ?? []).Where(memo => memo.Id != id).ToArray());
    }

    internal static async Task<string> TranscribeAsync(Guid id, string key, CancellationToken token)
    {
        var path = PathFor(id);
        Duration(path);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/audio/transcriptions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        var form = new MultipartFormDataContent();
        request.Content = form;
        var audio = new StreamContent(File.OpenRead(path));
        audio.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(audio, "file", "recording.wav");
        form.Add(new StringContent("whisper-large-v3-turbo"), "model");
        form.Add(new StringContent("json"), "response_format");
        using var response = await Http.SendAsync(request, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: token).ConfigureAwait(false);
        var text = payload == null ? "" : AgentSessions.String(payload.RootElement, "text")?.Trim() ?? "";
        return text[..Math.Min(text.Length, 24_000)];
    }

    private static TimeSpan Duration(string path)
    {
        var length = new FileInfo(path).Length;
        if (length is < 45 or > FileLimit)
            throw new IOException("Choose a non-empty WAV file smaller than 24 MB.");
        using var reader = new WaveFileReader(path);
        if (reader.Length == 0)
            throw new IOException("There is no audio in this recording.");
        return reader.TotalTime;
    }

    private static string CleanTitle(string title)
    {
        title = title.Trim();
        return title.Length == 0 ? "Voice memo" : title[..Math.Min(title.Length, 120)];
    }
}
