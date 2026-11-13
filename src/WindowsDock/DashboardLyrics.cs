using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace WindowsDock;

public partial class DashboardWindow
{
    private static readonly HttpClient LyricsClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly Regex LrcTag = new(@"\[(\d{1,4}):(\d{2})(?:[.:](\d{1,3}))?\]", RegexOptions.Compiled);
    private readonly Dictionary<string, LyricLine[]> lyricCache = new();
    private bool lyricsOpen;
    private string? loadingLyrics;
    private string? lyricError;

    private Border LyricsCard(out Action refresh)
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(Text("Lyrics", 11, subdued, true));
        var previous = Text("", 12, muted);
        var active = Text("", 16, strong: true);
        var upcoming = Text("", 12, subdued);
        previous.Margin = active.Margin = upcoming.Margin = new Thickness(0, 4, 0, 0);
        stack.Children.Add(previous);
        stack.Children.Add(active);
        stack.Children.Add(upcoming);
        refresh = () =>
        {
            previous.Text = upcoming.Text = "";
            if (playback is not { } song)
            {
                active.Text = "Start a track to see lyrics.";
                return;
            }
            if (!lyricCache.TryGetValue(song.Identity, out var lines))
            {
                if (loadingLyrics == null && lyricError != song.Identity)
                    _ = FetchLyricsAsync(song);
                active.Text = lyricError == song.Identity ? "Lyrics are temporarily unavailable." : "Finding lyrics…";
                return;
            }
            if (lines.Length == 0)
            {
                active.Text = "No synced lyrics for this track.";
                return;
            }

            var index = Array.FindLastIndex(lines, line => line.At <= song.Position);
            active.Text = index >= 0 ? lines[index].Text : "♪";
            previous.Text = index > 0 ? lines[index - 1].Text : "";
            upcoming.Text = index + 1 < lines.Length ? lines[index + 1].Text : "";
        };
        return Card(stack);
    }

    private async Task FetchLyricsAsync(Playback song)
    {
        loadingLyrics = song.Identity;
        try
        {
            if (song.Duration <= TimeSpan.Zero || string.IsNullOrWhiteSpace(song.Artist))
            {
                lyricCache[song.Identity] = [];
                return;
            }
            var url = FormattableString.Invariant(
                $"https://lrclib.net/api/get-cached?track_name={Uri.EscapeDataString(song.Title)}&artist_name={Uri.EscapeDataString(song.Artist)}&album_name={Uri.EscapeDataString(song.Album)}&duration={Math.Round(song.Duration.TotalSeconds)}");
            using var response = await LyricsClient.GetAsync(url, lifetime.Token);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                lyricCache[song.Identity] = [];
                return;
            }
            response.EnsureSuccessStatusCode();
            using var result = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: lifetime.Token);
            var lrc = result?.RootElement.TryGetProperty("syncedLyrics", out var synced) == true &&
                synced.ValueKind == JsonValueKind.String ? synced.GetString() : null;
            if (lyricCache.Count >= 20)
                lyricCache.Clear();
            lyricCache[song.Identity] = ParseLyrics(lrc);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or JsonException or InvalidOperationException)
        {
            lyricError = song.Identity;
        }
        finally { loadingLyrics = null; }
    }

    private static LyricLine[] ParseLyrics(string? lrc)
    {
        if (lrc == null)
            return [];

        var lines = new List<LyricLine>();
        foreach (var row in lrc.Split('\n'))
        {
            var tags = LrcTag.Matches(row);
            if (tags.Count == 0)
                continue;
            var last = tags[^1];
            var lyric = row[(last.Index + last.Length)..].Trim();
            foreach (Match tag in tags)
            {
                var minutes = int.Parse(tag.Groups[1].Value, CultureInfo.InvariantCulture);
                var seconds = int.Parse(tag.Groups[2].Value, CultureInfo.InvariantCulture);
                var fraction = tag.Groups[3].Value.PadRight(3, '0');
                var milliseconds = int.Parse(fraction, CultureInfo.InvariantCulture);
                lines.Add(new LyricLine(TimeSpan.FromMilliseconds((minutes * 60 + seconds) * 1000L + milliseconds), lyric));
            }
        }
        return lines.OrderBy(line => line.At).ToArray();
    }

    private sealed record LyricLine(TimeSpan At, string Text);
}
