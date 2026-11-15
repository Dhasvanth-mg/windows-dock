using NAudio.CoreAudioApi;
using Windows.Media.Control;
using System.Threading;
using System.Threading.Tasks;

namespace WindowsDock;

internal static class DockAudio
{
    internal static (int Percent, bool Muted) Output() =>
        WithEndpoint(endpoint => ((int)Math.Round(endpoint.MasterVolumeLevelScalar * 100), endpoint.Mute));

    internal static int SetVolume(double percent) => WithEndpoint(endpoint =>
    {
        endpoint.MasterVolumeLevelScalar = (float)(percent / 100);
        return (int)Math.Round(endpoint.MasterVolumeLevelScalar * 100);
    });

    internal static bool SetMute(bool muted) => WithEndpoint(endpoint =>
    {
        endpoint.Mute = muted;
        return endpoint.Mute;
    });

    internal static string[] Outputs()
    {
        using var audio = new MMDeviceEnumerator();
        var names = new List<string>();
        foreach (var device in audio.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            using (device) { names.Add(device.FriendlyName); }
        return names.ToArray();
    }

    internal static string SetOutput(string name)
    {
        using var audio = new MMDeviceEnumerator();
        var matches = new List<string>();
        foreach (var device in audio.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            using (device)
                if (device.FriendlyName.Equals(name, StringComparison.OrdinalIgnoreCase) || device.ID == name)
                    matches.Add(device.ID);
        if (matches.Count != 1)
            throw new InvalidOperationException("Choose one available audio output by its exact name.");
        AudioRouting.SetDefault(matches[0]);
        using var output = audio.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        if (output.ID != matches[0])
            throw new InvalidOperationException("Windows has not confirmed the new default audio output.");
        return output.FriendlyName;
    }

    internal static async Task<string> PlaybackAsync(string command, CancellationToken token)
    {
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(token);
        var session = manager.GetCurrentSession() ?? throw new InvalidOperationException("There is no active media session.");
        var operation = command switch
        {
            "play" => session.TryPlayAsync(),
            "pause" => session.TryPauseAsync(),
            "next" => session.TrySkipNextAsync(),
            _ => session.TrySkipPreviousAsync()
        };
        if (!await operation.AsTask(token))
            throw new InvalidOperationException("The current media session declined that playback command.");
        return command switch { "play" => "Resumed playback.", "pause" => "Paused playback.", "next" => "Skipped to the next track.", _ => "Went to the previous track." };
    }

    private static T WithEndpoint<T>(Func<AudioEndpointVolume, T> use)
    {
        using var audio = new MMDeviceEnumerator();
        using var output = audio.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        using var endpoint = output.AudioEndpointVolume;
        return use(endpoint);
    }
}
