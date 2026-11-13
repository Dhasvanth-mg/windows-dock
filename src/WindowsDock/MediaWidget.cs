using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using NAudio.CoreAudioApi;

namespace WindowsDock;

public partial class MainWindow
{
    private void AddMediaWidget()
    {
        var menu = new ContextMenu { MinWidth = 320 };
        var button = AddButton("Media and volume mixer", "♫", () => menu.IsOpen = true);
        AttachMenu(button, menu);
        menu.Opened += (_, _) => PopulateMedia(menu);
        menu.Closed += (_, _) => menu.Items.Clear();
    }

    private void PopulateMedia(ContextMenu menu)
    {
        AddMenuItem(menu, "Previous track", () => MediaKey(0xB1));
        AddMenuItem(menu, "Play / pause", () => MediaKey(0xB3));
        AddMenuItem(menu, "Next track", () => MediaKey(0xB0));
        menu.Items.Add(new Separator());
        AddMenuItem(menu, "Open Windows volume mixer", () => Launch("ms-settings:apps-volume"));
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            using var endpoint = device.AudioEndpointVolume;
            AddVolume("Output", endpoint.MasterVolumeLevelScalar, endpoint.Mute,
                (volume, mute) => WithDevice(current =>
                {
                    using var output = current.AudioEndpointVolume;
                    if (volume.HasValue) output.MasterVolumeLevelScalar = volume.Value;
                    if (mute.HasValue) output.Mute = mute.Value;
                }));

            var sessions = device.AudioSessionManager.Sessions;
            for (var index = 0; index < sessions.Count; index++)
            {
                using var session = sessions[index];
                if (session.State == NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateExpired)
                    continue;
                var id = session.GetSessionInstanceIdentifier;
                var volume = session.SimpleAudioVolume;
                AddVolume(SessionLabel(session), volume.Volume, volume.Mute, (level, mute) =>
                    WithDevice(current =>
                    {
                        var currentSessions = current.AudioSessionManager.Sessions;
                        for (var i = 0; i < currentSessions.Count; i++)
                        {
                            using var candidate = currentSessions[i];
                            if (candidate.GetSessionInstanceIdentifier != id)
                                continue;
                            if (level.HasValue) candidate.SimpleAudioVolume.Volume = level.Value;
                            if (mute.HasValue) candidate.SimpleAudioVolume.Mute = mute.Value;
                            break;
                        }
                    }));
            }
        }
        catch (COMException)
        {
            menu.Items.Add(new MenuItem { Header = "Audio device unavailable", IsEnabled = false });
        }

        void AddVolume(string name, float level, bool muted, Action<float?, bool?> change)
        {
            var row = new StackPanel { Margin = new Thickness(10, 5, 10, 5) };
            var slider = new Slider { Minimum = 0, Maximum = 100, Value = level * 100, Width = 220 };
            System.Windows.Automation.AutomationProperties.SetName(slider, name + " volume");
            var mute = new CheckBox { Content = "Mute " + name, IsChecked = muted, Foreground = System.Windows.Media.Brushes.White };
            var entry = new MenuItem { Header = row, StaysOpenOnClick = true, Focusable = false };
            var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
            row.Children.Add(new TextBlock { Text = name });
            row.Children.Add(slider);
            row.Children.Add(mute);
            row.Children.Add(status);
            slider.ValueChanged += (_, e) => TryChange((float)(e.NewValue / 100), null);
            mute.Checked += (_, _) => TryChange(null, true);
            mute.Unchecked += (_, _) => TryChange(null, false);
            menu.Items.Add(entry);

            void TryChange(float? value, bool? isMuted)
            {
                try { change(value, isMuted); }
                catch (COMException) { status.Text = "Audio output changed. Reopen the mixer."; }
            }
        }
    }

    private static void WithDevice(Action<MMDevice> change)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        change(device);
    }

    private static string SessionLabel(AudioSessionControl session)
    {
        if (session.IsSystemSoundsSession)
            return "System sounds";
        if (!string.IsNullOrWhiteSpace(session.DisplayName))
            return session.DisplayName;
        try
        {
            using var process = Process.GetProcessById((int)session.GetProcessID);
            return process.ProcessName;
        }
        catch (ArgumentException) { return "Audio session"; }
        catch (InvalidOperationException) { return "Audio session"; }
    }

    private static void MediaKey(byte key)
    {
        keybd_event(key, 0, 0, 0);
        keybd_event(key, 0, 2, 0);
    }

    [DllImport("user32")]
    private static extern void keybd_event(byte key, byte scan, uint flags, nuint extraInfo);
}
