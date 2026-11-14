using System.IO;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace WindowsDock;

internal sealed record AppAudioLevel(string Path, float Volume, bool Muted);
internal sealed record AudioPreset(string Name, string DeviceId, string DeviceName, float Volume, bool Muted, AppAudioLevel[] Apps)
{
    public string Label => $"{Name} · {DeviceName}";
}

internal static class AudioPresets
{
    internal static AudioPreset Capture(string name)
    {
        using var audio = new MMDeviceEnumerator();
        using var device = audio.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        using var master = device.AudioEndpointVolume;
        var apps = new Dictionary<string, AppAudioLevel>(StringComparer.OrdinalIgnoreCase);
        Visit(audio, (session, path) =>
        {
            var volume = session.SimpleAudioVolume;
            apps.TryAdd(path, new(path, volume.Volume, volume.Mute));
        });
        return new(name, device.ID, device.FriendlyName, master.MasterVolumeLevelScalar, master.Mute,
            apps.Values.Take(64).ToArray());
    }

    internal static string Apply(AudioPreset preset)
    {
        using var audio = new MMDeviceEnumerator();
        MMDevice? output = null;
        foreach (var device in audio.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            if (output == null && device.ID.Equals(preset.DeviceId, StringComparison.OrdinalIgnoreCase))
                output = device;
            else
                device.Dispose();
        }
        if (output == null)
            return $"Connect {preset.DeviceName} to apply this preset.";
        using (output)
        {
            using var current = audio.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (!current.ID.Equals(output.ID, StringComparison.OrdinalIgnoreCase))
                AudioRouting.SetDefault(output.ID);
            using var master = output.AudioEndpointVolume;
            master.MasterVolumeLevelScalar = Math.Clamp(preset.Volume, 0, 1);
            master.Mute = preset.Muted;
        }
        var levels = preset.Apps.GroupBy(app => app.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var applied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Visit(audio, (session, path) =>
        {
            if (!levels.TryGetValue(path, out var level))
                return;
            var volume = session.SimpleAudioVolume;
            volume.Volume = Math.Clamp(level.Volume, 0, 1);
            volume.Mute = level.Muted;
            applied.Add(path);
        });
        var missing = levels.Count - applied.Count;
        var message = $"{preset.Name} applied to {preset.DeviceName}.";
        return missing > 0 ? $"{message} {missing} saved app(s) had no available audio session. Reapply after opening them." : message;
    }

    private static void Visit(MMDeviceEnumerator audio, Action<AudioSessionControl, string> action)
    {
        var paths = new Dictionary<uint, string>();
        foreach (var output in audio.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (output)
            {
                try
                {
                    var sessions = output.AudioSessionManager.Sessions;
                    for (var index = 0; index < sessions.Count; index++)
                    {
                        try
                        {
                            using var session = sessions[index];
                            if (session.State == AudioSessionState.AudioSessionStateExpired || session.IsSystemSoundsSession)
                                continue;
                            var pid = session.GetProcessID;
                            if (!paths.TryGetValue(pid, out var path))
                                paths[pid] = path = ShellIntegration.ProcessPath(pid);
                            if (path.Length > 0)
                                action(session, path);
                        }
                        catch (Exception error) when (error is COMException or InvalidOperationException) { }
                    }
                }
                catch (COMException) { }
            }
        }
    }
}
