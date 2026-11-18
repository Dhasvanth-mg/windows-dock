using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NAudio.CoreAudioApi;
using Windows.Devices.Radios;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace WindowsDock;

public partial class DashboardWindow
{
    private GlobalSystemMediaTransportControlsSessionManager? mediaManager;
    private Task<GlobalSystemMediaTransportControlsSessionManager>? mediaInitialization;
    private Playback? playback;
    private bool readingMedia;
    private DateTime nextMediaRead;
    private DateTime mediaRetryAfter;
    private Action? updateAudioWidget;

    private void ShowAudioWidget()
    {
        var (window, content, cancellation) = CreateDockPanel("Audio switchboard", "", 524, 0xE767, purple, widget: true);
        BuildPinnedAudio(content, cancellation);
        window.Show();
    }

    private void BuildPinnedAudio(StackPanel content, System.Threading.CancellationToken cancellation, bool compact = false)
    {
        var player = NowPlaying(out var refresh, compact ? 48 : 64, compact: true);
        content.Children.Add(player);
        if (compact)
        {
            var slider = OutputSlider();
            slider.Margin = new Thickness(0, 6, 0, 0);
            content.Children.Add(slider);
        }
        else
        {
            content.Children.Add(Rule(12, 12));
            content.Children.Add(MixerCard(bare: true));
        }
        updateAudioWidget = refresh;
        cancellation.Register(() => updateAudioWidget = null);
        refresh();
    }

    // Artwork, what is playing, and the transport. Compact puts the buttons beside the title; otherwise underneath.
    private FrameworkElement NowPlaying(out Action refresh, double artSize, bool compact, bool lyricsToggle = false)
    {
        var artwork = new Image { Width = artSize, Height = artSize, Stretch = Stretch.UniformToFill };
        var note = DockGlyph(0xE8D6, artSize * .3, AssistantFaint);
        note.HorizontalAlignment = HorizontalAlignment.Center;
        var art = new Grid();
        art.Children.Add(note);
        art.Children.Add(artwork);
        var corner = artSize >= 90 ? 10 : 8;
        var frame = new Border
        {
            Width = artSize, Height = artSize, Background = ControlFill, CornerRadius = new CornerRadius(corner), Child = art,
            Margin = new Thickness(0, 0, compact ? 12 : 18, 0), VerticalAlignment = VerticalAlignment.Top,
            Clip = new RectangleGeometry(new Rect(0, 0, artSize, artSize), corner, corner)
        };

        var transport = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        transport.Children.Add(IconButton(0xE892, "Previous track", () => ControlPlayback(0xB1)));
        var play = IconButton(0xE768, "Play or pause", () => ControlPlayback(0xB3));
        MarkPrimary(play, true);
        play.Width = 38;
        play.Margin = new Thickness(4, 0, 4, 0);
        transport.Children.Add(play);
        transport.Children.Add(IconButton(0xE893, "Next track", () => ControlPlayback(0xB0)));

        var title = Text("", compact ? 15 : 20, strong: true);
        var artist = Text("", compact ? 12 : 13, subdued);
        artist.Margin = new Thickness(0, 2, 0, 0);
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(title);
        labels.Children.Add(artist);

        var progress = Progress(AssistantInk);
        progress.Height = 3;
        var elapsed = Text("", 11, muted);
        var length = Text("", 11, muted);
        Typography.SetNumeralAlignment(elapsed, FontNumeralAlignment.Tabular);
        Typography.SetNumeralAlignment(length, FontNumeralAlignment.Tabular);
        var timeline = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
        elapsed.Margin = new Thickness(0, 0, 9, 0);
        length.Margin = new Thickness(9, 0, 0, 0);
        DockPanel.SetDock(length, Dock.Right);
        timeline.Children.Add(elapsed);
        timeline.Children.Add(length);
        progress.VerticalAlignment = VerticalAlignment.Center;
        timeline.Children.Add(progress);

        var row = new DockPanel { LastChildFill = true };
        row.Children.Add(frame);
        var body = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
        if (compact)
        {
            timeline.Margin = new Thickness(0, 8, 0, 0);
            DockPanel.SetDock(timeline, Dock.Bottom);
            body.Children.Add(timeline);
            transport.Margin = new Thickness(10, 0, -3, 0);
            DockPanel.SetDock(transport, Dock.Right);
            body.Children.Add(transport);
            body.Children.Add(labels);
        }
        else
        {
            var stack = new StackPanel();
            stack.Children.Add(labels);
            timeline.Margin = new Thickness(0, 14, 0, 10);
            stack.Children.Add(timeline);
            var actions = new DockPanel();
            if (lyricsToggle)
            {
                var lyrics = QuietButton(lyricsOpen ? "Back to mixer" : "Lyrics", () =>
                {
                    lyricsOpen = !lyricsOpen;
                    lyricError = null;
                    ShowPage(DashboardPage.Controls);
                });
                lyrics.ToolTip = lyricsOpen ? null : "Looks up synced lyrics on LRCLIB using the track name and artist.";
                ToolTipService.SetIsEnabled(lyrics, !lyricsOpen);
                lyrics.VerticalAlignment = VerticalAlignment.Center;
                DockPanel.SetDock(lyrics, Dock.Right);
                actions.Children.Add(lyrics);
            }
            transport.Margin = new Thickness(-4, 0, 0, 0);
            transport.HorizontalAlignment = HorizontalAlignment.Left;
            actions.Children.Add(transport);
            stack.Children.Add(actions);
            body.Children.Add(stack);
        }
        row.Children.Add(body);

        refresh = () =>
        {
            _ = RefreshPlaybackAsync();
            title.Text = playback?.Title ?? "Nothing playing";
            artist.Text = playback == null ? "Start something and it shows up here." :
                string.IsNullOrWhiteSpace(playback.Artist) ? playback.Album : playback.Artist;
            title.ToolTip = playback?.Title;
            artwork.Source = playback?.Artwork;
            ((TextBlock)play.Content).Text = playback?.Playing == true ? "" : "";
            AutomationProperties.SetName(play, playback?.Playing == true ? "Pause" : "Play");
            if (playback is { } current && current.Duration > TimeSpan.Zero)
            {
                var position = Math.Clamp(LivePosition(current).TotalSeconds, 0, current.Duration.TotalSeconds);
                progress.Value = position / current.Duration.TotalSeconds * 100;
                elapsed.Text = FormatTime(TimeSpan.FromSeconds(position));
                length.Text = FormatTime(current.Duration);
                timeline.Visibility = Visibility.Visible;
            }
            else
            {
                progress.Value = 0;
                elapsed.Text = length.Text = "";
                timeline.Visibility = compact ? Visibility.Collapsed : Visibility.Hidden;
            }
        };
        return row;
    }

    private async Task RefreshPlaybackAsync()
    {
        // Asking Windows for the session is a cross-process call, so do it every few seconds rather than every tick.
        // The position between reads is worked out from the clock (see LivePosition).
        if (readingMedia || DateTime.UtcNow < mediaRetryAfter || DateTime.UtcNow < nextMediaRead)
            return;
        readingMedia = true;
        try
        {
            mediaManager ??= await (mediaInitialization ??= GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(lifetime.Token));
            var session = mediaManager.GetCurrentSession();
            if (session == null)
            {
                playback = null;
                return;
            }
            var info = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();
            var item = await session.TryGetMediaPropertiesAsync().AsTask(lifetime.Token);
            if (item == null || string.IsNullOrWhiteSpace(item.Title))
            {
                playback = null;
                return;
            }
            var artwork = playback?.Artwork;
            var identity = session.SourceAppUserModelId + "\n" + item.Title + "\n" + item.Artist + "\n" + item.AlbumTitle;
            if (playback?.Identity != identity)
            {
                artwork = null;
                if (item.Thumbnail != null)
                {
                    try
                    {
                        using var stream = await item.Thumbnail.OpenReadAsync().AsTask(lifetime.Token);
                        if (stream.Size is > 0 and <= 8_000_000)
                        {
                            using var reader = new DataReader(stream.GetInputStreamAt(0));
                            var length = await reader.LoadAsync((uint)stream.Size).AsTask(lifetime.Token);
                            var buffer = new byte[length];
                            reader.ReadBytes(buffer);
                            using var input = new MemoryStream(buffer, false);
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.StreamSource = input;
                            bitmap.DecodePixelWidth = 160;
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.EndInit();
                            bitmap.Freeze();
                            artwork = bitmap;
                        }
                    }
                    catch (Exception error) when (error is COMException or System.IO.IOException or NotSupportedException or ArgumentException) { }
                }
            }
            var playing = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var elapsed = playing ? DateTimeOffset.UtcNow - timeline.LastUpdatedTime : TimeSpan.Zero;
            if (elapsed < TimeSpan.Zero || elapsed > TimeSpan.FromDays(1))
                elapsed = TimeSpan.Zero;
            nextMediaRead = DateTime.UtcNow.AddSeconds(4);
            playback = new Playback(identity, item.Title, item.Artist, item.AlbumTitle, artwork, playing,
                timeline.Position - timeline.StartTime + elapsed, timeline.EndTime - timeline.StartTime, DateTime.UtcNow);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            mediaInitialization = null;
            mediaManager = null;
            playback = null;
            mediaRetryAfter = DateTime.UtcNow.AddSeconds(15);
        }
        finally
        {
            readingMedia = false;
            if (nextMediaRead < DateTime.UtcNow)
                nextMediaRead = DateTime.UtcNow.AddSeconds(playback == null ? 2 : 4);
        }
    }

    // Controls: sound on the left (where it plays, how loud, each app), everything you switch or set on the right.
    private UIElement ControlsPage()
    {
        var page = FillPage(SplitPage(1.08, 1, out var left, out var right));

        if (lyricsOpen)
        {
            left.Children.Add(NowPlaying(out var updateMedia, 64, compact: true));
            left.Children.Add(Rule(14, 14));
            left.Children.Add(LyricsCard(out var refreshLyrics));
            var back = QuietButton("Back to the mixer", ToggleLyrics);
            back.HorizontalAlignment = HorizontalAlignment.Left;
            back.Margin = new Thickness(0, 14, 0, 0);
            left.Children.Add(back);
            updatePage = () => { updateMedia(); refreshLyrics(); };
        }
        else
        {
            left.Children.Add(MixerCard(bare: true));
            var lyrics = QuietButton("Lyrics for what is playing", ToggleLyrics);
            lyrics.ToolTip = "Looks up synced lyrics on LRCLIB using the track name and artist.";
            ToolTipService.SetIsEnabled(lyrics, true);
            lyrics.HorizontalAlignment = HorizontalAlignment.Left;
            lyrics.Margin = new Thickness(0, 10, 0, 0);
            left.Children.Add(lyrics);
        }

        right.Children.Add(RadioRow(RadioKind.WiFi, "Networks"));
        right.Children.Add(RadioRow(RadioKind.Bluetooth, "Devices"));
        var startup = Switch("Start at sign-in", DockStartup.Enabled());
        startup.Click += (_, _) =>
        {
            try { DockStartup.Set(startup.IsChecked == true); }
            catch (Exception error) when (DockActions.IsActionError(error))
            {
                startup.IsChecked = DockStartup.Enabled();
                ShowOwnedMessage(error.Message);
            }
        };
        right.Children.Add(SettingRow("Start at sign-in", null, startup));
        right.Children.Add(SettingRow("Accent", chosenAccent.Name, AccentPicker()));
        right.Children.Add(WeatherSettings());
        right.Children.Add(SettingRow("Taskbar", trayMode
            ? "Tray mode is on. Ctrl+Alt+D opens coding chats from anywhere."
            : "Pins, tray icons and auto-hide are still Windows settings.",
            ActionButton("Taskbar settings", () => OpenTarget("ms-settings:taskbar"))));
        if (controllerPid > 0)
        {
            var leave = ActionButton(trayMode ? "Exit" : "Restore", () =>
            {
                if (System.Threading.EventWaitHandle.TryOpenExisting(
                    @"Local\WindowsDock.NativeTaskbar." + controllerPid + ".Stop", out var stop))
                {
                    using (stop)
                        stop.Set();
                }
                Close();
            });
            AutomationProperties.SetName(leave, trayMode ? "Exit Windows Dock" : "Restore taskbar appearance");
            right.Children.Add(SettingRow(trayMode ? "Exit Windows Dock" : "Restore the normal taskbar",
                trayMode ? "Closes the tray icon and the dashboard." : "Puts Explorer's taskbar back and closes Windows Dock.",
                leave));
        }

        var more = SectionTitle("More in Windows settings");
        more.Margin = new Thickness(0, 12, 0, 2);
        right.Children.Add(more);
        var links = new WrapPanel();
        foreach (var (name, path) in new[]
        {
            ("Display", "display"), ("Night light", "nightlight"), ("Notifications", "notifications"),
            ("Focus", "quiethours"), ("Sound", "sound"), ("Ethernet", "network-ethernet"),
            ("Power", "powersleep"), ("Accessibility", "easeofaccess")
        })
        {
            var link = QuietButton(name, () => OpenTarget("ms-settings:" + path));
            AutomationProperties.SetName(link, name + " settings");
            link.Margin = new Thickness(0, 0, 16, 2);
            links.Children.Add(link);
        }
        right.Children.Add(links);
        return page;

        void ToggleLyrics()
        {
            lyricsOpen = !lyricsOpen;
            lyricError = null;
            ShowPage(DashboardPage.Controls);
        }
    }

    private Border RadioRow(RadioKind kind, string settingsLabel)
    {
        var name = DockRadios.Label(kind);
        var toggle = Switch(name);
        toggle.IsEnabled = false;
        var link = QuietButton(settingsLabel, kind == RadioKind.WiFi ? ShowWifiWidget : ShowBluetoothWidget);
        link.Margin = new Thickness(0, 0, 4, 0);
        var trailing = new StackPanel { Orientation = Orientation.Horizontal };
        trailing.Children.Add(link);
        toggle.Margin = new Thickness(12, 0, 0, 0);
        trailing.Children.Add(toggle);
        var row = SettingRow(name, "Checking…", trailing, out var caption);
        toggle.Loaded += async (_, _) => await Refresh();
        toggle.Click += async (_, _) =>
        {
            var wanted = toggle.IsChecked == true;
            toggle.IsEnabled = false;
            string? problem = null;
            try { await DockRadios.SetAsync(kind, wanted, lifetime.Token); }
            catch (OperationCanceledException) { }
            catch (Exception error) when (DockActions.IsActionError(error)) { problem = error.Message; }
            await Refresh();
            if (problem != null)
                caption.Text = problem;
        };
        return row;

        async Task Refresh()
        {
            var state = "unavailable";
            try { state = await DockRadios.StateAsync(kind, lifetime.Token); }
            catch (OperationCanceledException) { }
            catch (Exception error) when (DockActions.IsActionError(error)) { }
            var on = state == "On";
            toggle.IsChecked = on;
            toggle.IsEnabled = state is not ("unavailable" or "Disabled");
            caption.Text = state switch
            {
                "On" => "On",
                "Off" => "Off",
                "Disabled" => "Turned off by Windows or a hardware switch",
                "unavailable" => "This PC has no " + name + " radio Windows will share",
                _ => state
            };
            caption.Visibility = Visibility.Visible;
            AutomationProperties.SetName(toggle, $"{name}. {caption.Text}");
        }
    }

    // Talking to Windows audio can take a large fraction of a second, so none of it happens on the interface thread.
    // Controls appear at once with the last known values and catch up when the real ones arrive.
    private static MixerState? lastMixer;
    private static int? lastOutputPercent;

    private static bool IsAudioError(Exception error) =>
        error is COMException or InvalidCastException or UnauthorizedAccessException or InvalidOperationException or ArgumentException;

    // Runs work in the background, newest value only: dragging a slider never queues up a backlog of old positions.
    private Action<T> LatestOnly<T>(Action<T> work, Action failed)
    {
        var gate = new object();
        var pending = default(T)!;
        var waiting = false;
        var running = false;
        return value =>
        {
            lock (gate)
            {
                pending = value;
                waiting = true;
                if (running)
                    return;
                running = true;
            }
            _ = Task.Run(() =>
            {
                while (true)
                {
                    T next;
                    lock (gate)
                    {
                        if (!waiting)
                        {
                            running = false;
                            return;
                        }
                        next = pending;
                        waiting = false;
                    }
                    try { work(next); }
                    catch (Exception error) when (IsAudioError(error)) { Dispatcher.BeginInvoke(failed); }
                }
            });
        };
    }

    private Slider OutputSlider(int? known = null)
    {
        var slider = Range("Output volume", 100, known ?? lastOutputPercent ?? 0);
        var loading = false;
        var send = LatestOnly<double>(value => lastOutputPercent = DockAudio.SetVolume(value), () => slider.IsEnabled = false);
        slider.ValueChanged += (_, e) =>
        {
            if (!loading)
                send(e.NewValue);
        };
        if (known == null)
            _ = LoadAsync();
        return slider;

        async Task LoadAsync()
        {
            try
            {
                var percent = await Task.Run(() => DockAudio.Output().Percent);
                lastOutputPercent = percent;
                if (slider.IsMouseCaptureWithin)
                    return;
                loading = true;
                slider.Value = percent;
                loading = false;
            }
            catch (Exception error) when (IsAudioError(error))
            {
                slider.IsEnabled = false;
                slider.ToolTip = "No active output device";
            }
        }
    }

    private sealed record MixerSession(AudioOutput Output, string Id, uint Pid, string Name, bool IsSystem, double Volume,
        string? Route, bool RouteKnown);

    private sealed record MixerState(AudioOutput[] Devices, string? CurrentId, bool Muted, int Percent, MixerSession[] Sessions)
    {
        internal string Signature => string.Join("|", Devices.Select(item => item.Id)) + "#" + CurrentId + "#" + Muted + "#" + Percent + "#" +
            string.Join("|", Sessions.Select(item => $"{item.Id}:{item.Name}:{item.Volume:0}:{item.Route}:{item.RouteKnown}"));
    }

    private static MixerState ReadMixer()
    {
        using var enumerator = new MMDeviceEnumerator();
        var devices = new List<AudioOutput>();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            using (device)
                devices.Add(new(device.ID, device.FriendlyName));
        using var current = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        using var endpoint = current.AudioEndpointVolume;
        var found = new List<MixerSession>();
        foreach (var output in devices)
        {
            using var device = enumerator.GetDevice(output.Id);
            var active = device.AudioSessionManager.Sessions;
            for (var index = 0; index < active.Count && found.Count < 24; index++)
            {
                using var session = active[index];
                if (session.State == NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateExpired)
                    continue;
                var pid = session.GetProcessID;
                var system = session.IsSystemSoundsSession;
                string? route = null;
                var routeKnown = false;
                if (pid != 0 && !system)
                    try
                    {
                        route = AudioRouting.AppRoute(pid);
                        routeKnown = true;
                    }
                    catch (Exception error) when (error is COMException or UnauthorizedAccessException) { }
                found.Add(new(output, session.GetSessionInstanceIdentifier, pid, system ? "System sounds" : AudioSessionName(session),
                    system, session.SimpleAudioVolume.Volume * 100, route, routeKnown));
            }
        }
        return new(devices.ToArray(), current.ID, endpoint.Mute, (int)Math.Round(endpoint.MasterVolumeLevelScalar * 100), found.ToArray());
    }

    private Border MixerCard(bool bare = false)
    {
        var body = new StackPanel();
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var external = IconButton(0xE8A7, "Open the Windows volume mixer", () => OpenTarget("ms-settings:apps-volume"));
        DockPanel.SetDock(external, Dock.Right);
        top.Children.Add(external);
        var refresh = IconButton(0xE72C, "Refresh devices and apps");
        DockPanel.SetDock(refresh, Dock.Right);
        top.Children.Add(refresh);
        var presets = QuietButton("Presets", ShowAudioPresets);
        presets.Margin = new Thickness(0, 0, 10, 0);
        presets.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(presets, Dock.Right);
        top.Children.Add(presets);
        var outputLabel = SectionTitle("Plays on");
        outputLabel.Margin = new Thickness(0);
        outputLabel.VerticalAlignment = VerticalAlignment.Center;
        top.Children.Add(outputLabel);
        body.Children.Add(top);
        var outputs = new ComboBox { Margin = new Thickness(0, 0, 0, 8), DisplayMemberPath = nameof(AudioOutput.Name) };
        AutomationProperties.SetName(outputs, "Audio output");
        var status = Text("", 11, subdued);
        status.TextWrapping = TextWrapping.Wrap;
        var system = new DockPanel();
        var mute = IconButton(0xE767, "Mute output");
        mute.Margin = new Thickness(-6, 0, 6, 0);
        system.Children.Add(mute);
        var volume = new ContentControl { VerticalAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        system.Children.Add(volume);
        body.Children.Add(outputs);
        body.Children.Add(system);
        body.Children.Add(status);
        var appHeading = SectionTitle("Apps");
        appHeading.Margin = new Thickness(0, 14, 0, 0);
        body.Children.Add(appHeading);
        var sessions = new StackPanel();
        body.Children.Add(new ScrollViewer { Content = sessions, MaxHeight = bare ? 176 : 154, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 4, 0, 0) });
        AudioOutput[] devices = [];
        var populating = false;
        var loadVersion = 0;
        string? shown = null;
        refresh.Click += (_, _) => Reload();
        mute.Click += async (_, _) =>
        {
            try
            {
                await Task.Run(() => AudioDevice(device =>
                {
                    using var endpoint = device.AudioEndpointVolume;
                    endpoint.Mute = !endpoint.Mute;
                }));
                Reload();
            }
            catch (Exception error) when (IsAudioError(error)) { status.Text = "The output is unavailable."; }
        };
        outputs.SelectionChanged += (_, _) =>
        {
            if (populating || outputs.SelectedItem is not AudioOutput selection)
                return;
            try
            {
                AudioRouting.SetDefault(selection.Id);
                Reload();
            }
            catch (Exception error) when (error is COMException or InvalidCastException or UnauthorizedAccessException)
            {
                Reload();
                status.Text = "Windows could not switch the output.";
            }
        };
        Reload();
        if (!bare)
            return Card(body);
        return new Border { Child = body };

        void Reload()
        {
            status.Text = "";
            var version = ++loadVersion;
            if (lastMixer is { } known)
                Present(known);
            _ = LoadAsync(version);
        }

        async Task LoadAsync(int version)
        {
            try
            {
                var state = await Task.Run(ReadMixer);
                lastMixer = state;
                lastOutputPercent = state.Percent;
                if (version == loadVersion)
                    Present(state);
            }
            catch (Exception error) when (IsAudioError(error))
            {
                if (version != loadVersion)
                    return;
                lastMixer = null;
                shown = null;
                devices = [];
                populating = true;
                outputs.ItemsSource = devices;
                populating = false;
                sessions.Children.Clear();
                sessions.Children.Add(Text("No apps are playing sound right now.", 12, muted));
                outputs.IsEnabled = mute.IsEnabled = false;
                status.Text = "No active audio output.";
            }
        }

        // Redraws only when something actually changed, so a refresh never disturbs the sliders.
        void Present(MixerState state)
        {
            if (state.Signature == shown)
                return;
            shown = state.Signature;
            populating = true;
            try
            {
                sessions.Children.Clear();
                volume.Content = OutputSlider(state.Percent);
                devices = state.Devices;
                outputs.ItemsSource = devices;
                outputs.SelectedItem = devices.FirstOrDefault(item => item.Id == state.CurrentId);
                ((TextBlock)mute.Content).Text = state.Muted ? "" : "";
                mute.ToolTip = state.Muted ? "Unmute output" : "Mute output";
                AutomationProperties.SetName(mute, (string)mute.ToolTip);
                foreach (var session in state.Sessions)
                    AddSession(session);
            }
            finally { populating = false; }
            outputs.IsEnabled = mute.IsEnabled = devices.Length > 0;
            if (sessions.Children.Count == 0)
                sessions.Children.Add(Text("No apps are playing sound right now.", 12, muted));
        }

        void AddSession(MixerSession session)
        {
            var output = session.Output;
            var id = session.Id;
            var pid = session.Pid;
            var name = session.Name;
            var row = Columns(1.05, 1, 0.85);
            row.Margin = new Thickness(0, 5, 0, 1);
            var label = Text(name, 12);
            label.VerticalAlignment = VerticalAlignment.Center;
            label.ToolTip = output.Name;
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            var labels = new StackPanel();
            labels.Children.Add(label);
            var endpointLabel = Text(output.Name, 11, muted);
            endpointLabel.TextTrimming = TextTrimming.CharacterEllipsis;
            endpointLabel.ToolTip = output.Name;
            labels.Children.Add(endpointLabel);
            Put(row, labels, 0);
            var level = Range(name + " volume", 100, session.Volume);
            var setLevel = LatestOnly<double>(value =>
            {
                using var enumerator = new MMDeviceEnumerator();
                using var device = enumerator.GetDevice(output.Id);
                var candidates = device.AudioSessionManager.Sessions;
                for (var index = 0; index < candidates.Count; index++)
                {
                    using var candidate = candidates[index];
                    if (candidate.GetSessionInstanceIdentifier != id)
                        continue;
                    candidate.SimpleAudioVolume.Volume = (float)(value / 100);
                    break;
                }
            }, () => level.IsEnabled = false);
            level.ValueChanged += (_, e) => setLevel(e.NewValue);
            Put(row, level, 1);
            if (pid != 0 && !session.IsSystem)
            {
                var automatic = new AudioOutput("", "System default");
                var choice = new ComboBox
                {
                    ItemsSource = new[] { automatic }.Concat(devices).ToArray(),
                    DisplayMemberPath = nameof(AudioOutput.Name), MinWidth = 75,
                    ToolTip = "Route " + name + " to an output"
                };
                AutomationProperties.SetName(choice, name + " output");
                AudioOutput? selected = null;
                if (session.RouteKnown)
                    selected = session.Route == null ? automatic : devices.FirstOrDefault(device => device.Id == session.Route);
                else
                {
                    choice.IsEnabled = false;
                    choice.ToolTip = "Per-app routing is unavailable on this build.";
                }
                choice.SelectedItem = selected ?? devices.FirstOrDefault(device => device.Id == output.Id);
                choice.SelectionChanged += async (_, _) =>
                {
                    if (choice.SelectedItem is not AudioOutput target)
                        return;
                    choice.IsEnabled = false;
                    try
                    {
                        var explicitOutput = string.IsNullOrEmpty(target.Id) ? null : target.Id;
                        var path = AudioRouting.RouteApplication(pid, explicitOutput);
                        var deviceId = explicitOutput;
                        if (deviceId == null)
                            AudioDevice(device => deviceId = device.ID);
                        status.Text = $"Checking {name} on {target.Name}…";
                        for (var attempt = 0; attempt < 8 && choice.IsLoaded; attempt++)
                        {
                            var actual = AudioRouting.PlayingOutputs(pid, path);
                            if (actual.Length == 0 || actual.All(id => id == deviceId))
                            {
                                Reload();
                                status.Text = actual.Length == 0
                                    ? $"{name}: {target.Name} saved for the next playback."
                                    : $"{name} is playing on {target.Name}.";
                                return;
                            }
                            await Task.Delay(450, lifetime.Token);
                        }
                        if (choice.IsLoaded)
                            status.Text = $"{name}: {target.Name} saved. Resume playback or reload the player to move its open stream.";
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception error) when (DockActions.IsActionError(error) || error is InvalidCastException)
                    {
                        Reload();
                        status.Text = $"Windows could not route {name}: {error.Message}";
                    }
                    finally { choice.IsEnabled = true; }
                };
                Put(row, choice, 2);
            }
            sessions.Children.Add(row);
        }
    }

    private sealed record AudioOutput(string Id, string Name);

    private static string AudioSessionName(AudioSessionControl session)
    {
        if (!string.IsNullOrWhiteSpace(session.DisplayName))
            return session.DisplayName;
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById((int)session.GetProcessID);
            return process.ProcessName;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            return "App audio";
        }
    }

    private static void AudioDevice(Action<MMDevice> action)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        action(device);
    }

    private static string FormatTime(TimeSpan time) => $"{(int)time.TotalMinutes}:{time.Seconds:00}";

    private async void ControlPlayback(byte key)
    {
        try
        {
            var session = mediaManager?.GetCurrentSession();
            if (session != null)
            {
                var action = key switch
                {
                    0xB1 => session.TrySkipPreviousAsync(),
                    0xB0 => session.TrySkipNextAsync(),
                    _ => session.TryTogglePlayPauseAsync()
                };
                var handled = await action.AsTask(lifetime.Token);
                // Look again straight away so the button shows the new state without waiting for the next slow read.
                nextMediaRead = default;
                await Task.Delay(220, lifetime.Token);
                if (!closed)
                    UpdateDashboard();
                if (handled)
                    return;
            }
        }
        catch (OperationCanceledException) { return; }
        catch (Exception error) when (error is COMException or InvalidOperationException or UnauthorizedAccessException) { }
        if (!closed)
            SendMediaKey(key);
    }

    private static void SendMediaKey(byte key)
    {
        keybd_event(key, 0, 0, 0);
        keybd_event(key, 0, 2, 0);
    }

    [DllImport("user32")]
    private static extern void keybd_event(byte key, byte scan, uint flags, nuint extraInfo);

    private sealed record Playback(string Identity, string Title, string Artist, string Album, ImageSource? Artwork,
        bool Playing, TimeSpan Position, TimeSpan Duration, DateTime ReadAt);

    private static TimeSpan LivePosition(Playback item) =>
        item.Playing ? item.Position + (DateTime.UtcNow - item.ReadAt) : item.Position;
}
