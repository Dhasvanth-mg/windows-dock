using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
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
    private DateTime mediaRetryAfter;
    private Action? updateAudioWidget;

    private UIElement HomePage()
    {
        var grid = Columns(1.55, 1);
        var media = MediaCard(false, out var updateMedia, overview: true);
        Put(grid, media, 0);
        Put(grid, QuickCard(), 1);

        var date = new DockPanel();
        var today = DateTime.Today;
        var dateNumber = Text(today.Day.ToString(), 24, amber, true);
        var dayTile = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var month = Text(today.ToString("MMM").ToUpperInvariant(), 9, amber, true);
        month.HorizontalAlignment = dateNumber.HorizontalAlignment = HorizontalAlignment.Center;
        dayTile.Children.Add(month);
        dayTile.Children.Add(dateNumber);
        var badge = new Border { Width = 53, Height = 60, Child = dayTile, CornerRadius = new CornerRadius(9),
            Background = AgentArt.Wash(amber, 31), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 11, 0) };
        date.Children.Add(badge);
        var calendarInfo = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var dateLabel = Text(today.ToString("dddd"), 13, strong: true);
        calendarInfo.Children.Add(dateLabel);
        var calendar = ActionButton("Open calendar", () => ShowPage(DashboardPage.Calendar));
        calendar.Padding = new Thickness(7, 4, 7, 4);
        calendar.Margin = new Thickness(0, 5, 0, 0);
        calendar.HorizontalAlignment = HorizontalAlignment.Left;
        calendarInfo.Children.Add(calendar);
        date.Children.Add(calendarInfo);

        var forecast = new DockPanel();
        var weatherArt = WeatherIllustration(weather?.Code);
        DockPanel.SetDock(weatherArt, Dock.Right);
        forecast.Children.Add(weatherArt);
        var weatherInfo = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        if (weather is { } currentWeather)
        {
            weatherInfo.Children.Add(Text(DisplayTemperature(currentWeather.Temperature), 24, strong: true));
            var location = Text(currentWeather.City, 11, subdued);
            location.TextTrimming = TextTrimming.CharacterEllipsis;
            weatherInfo.Children.Add(location);
        }
        else
            weatherInfo.Children.Add(Text(city?.Name ?? "Weather", 13, strong: true));
        var weatherLink = ActionButton(weather == null ? "Set location" : "View forecast",
            () => ShowPage(DashboardPage.Weather));
        weatherLink.Padding = new Thickness(7, 4, 7, 4);
        weatherLink.Margin = new Thickness(0, 5, 0, 0);
        weatherLink.HorizontalAlignment = HorizontalAlignment.Left;
        weatherInfo.Children.Add(weatherLink);
        forecast.Children.Add(weatherInfo);

        var overview = new StackPanel();
        var details = ActionButton("Details", () => ShowPage(DashboardPage.Stats));
        details.Padding = new Thickness(7, 4, 7, 4);
        details.VerticalAlignment = VerticalAlignment.Center;
        var readings = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(details, Dock.Right);
        readings.Children.Add(details);
        var meters = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        meters.Children.Add(OverviewGauge("CPU", green, out var cpu));
        meters.Children.Add(OverviewGauge("RAM", accent, out var memory));
        readings.Children.Add(meters);
        overview.Children.Add(readings);
        var sections = Columns(1, 1.1, 1.18);
        Put(sections, Card(date), 0);
        Put(sections, Card(forecast), 1);
        Put(sections, Card(overview), 2);
        Put(grid, sections, 1, 0, 2);
        updatePage = () =>
        {
            updateMedia();
            var current = DateTime.Today;
            dateLabel.Text = current.ToString("dddd");
            month.Text = current.ToString("MMM").ToUpperInvariant();
            dateNumber.Text = current.Day.ToString();
            var snapshot = ReadMetrics();
            cpu(snapshot.Cpu);
            memory(snapshot.Memory);
        };
        var content = new StackPanel();
        homeFavorites = new WrapPanel { Margin = new Thickness(1, 0, 0, 4) };
        RenderHomeFavorites();
        content.Children.Add(homeFavorites);
        content.Children.Add(grid);
        return content;
    }

    private UIElement MediaPage()
    {
        var grid = WideDashboard ? Columns(1.23, 1) : Columns(1);
        var card = MediaCard(true, out var update);
        Put(grid, card, 0);
        if (lyricsOpen)
        {
            Put(grid, LyricsCard(out var refreshLyrics), WideDashboard ? 0 : 1, WideDashboard ? 1 : 0);
            updatePage = () => { update(); refreshLyrics(); };
        }
        else
        {
            Put(grid, MixerCard(), WideDashboard ? 0 : 1, WideDashboard ? 1 : 0);
            updatePage = update;
        }
        return grid;
    }

    private void ShowAudioWidget()
    {
        var (window, content, cancellation) = CreateDockPanel("Audio switchboard", "", 524, 0xE767, purple, widget: true);
        BuildPinnedAudio(content, cancellation);
        window.Show();
    }

    private void BuildPinnedAudio(StackPanel content, System.Threading.CancellationToken cancellation, bool compact = false)
    {
        var playbackCard = MediaCard(!compact, out var refresh, widget: true);
        playbackCard.Margin = new Thickness(0, 0, 0, 7);
        content.Children.Add(playbackCard);
        content.Children.Add(compact ? OutputSlider() : MixerCard());
        updateAudioWidget = refresh;
        cancellation.Register(() => updateAudioWidget = null);
        refresh();
    }

    private Border MediaCard(bool expanded, out Action refresh, bool widget = false, bool overview = false)
    {
        var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var row = new DockPanel { LastChildFill = true };
        var artSize = overview ? 76 : expanded ? 66 : 45;
        var artwork = new Image { Width = artSize, Height = artSize, Stretch = Stretch.UniformToFill };
        var icon = DockGlyph(0xE8D6, 22, purple);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        var art = new Grid();
        art.Children.Add(icon);
        art.Children.Add(artwork);
        var artworkFrame = new Border { Width = artSize, Height = artSize, Background = AgentArt.Wash(purple, 38),
            CornerRadius = new CornerRadius(11), Child = art, Margin = new Thickness(0, 0, 10, 0),
            Clip = new RectangleGeometry(new Rect(0, 0, artSize, artSize), 11, 11) };
        row.Children.Add(artworkFrame);
        var transport = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 7, 0, 0) };
        transport.Children.Add(IconButton(0xE892, "Previous track", () => ControlPlayback(0xB1)));
        var play = IconButton(0xE768, "Play or pause", () => ControlPlayback(0xB3));
        play.Background = purple;
        play.Foreground = Brush("#20162D");
        play.Width = 36;
        transport.Children.Add(play);
        transport.Children.Add(IconButton(0xE893, "Next track", () => ControlPlayback(0xB0)));
        if (expanded && !widget)
        {
            var lyrics = IconButton(lyricsOpen ? 0xE9E9 : 0xE8A5, lyricsOpen ? "Show mixer" : "Find synced lyrics with LRCLIB", () =>
            {
                lyricsOpen = !lyricsOpen;
                lyricError = null;
                ShowPage(DashboardPage.Media);
            });
            transport.Children.Add(lyrics);
        }
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) };
        var title = Text("Nothing playing", expanded || overview ? 17 : 14, strong: true);
        var artist = Text("Windows media", 11, subdued);
        title.TextTrimming = artist.TextTrimming = TextTrimming.CharacterEllipsis;
        labels.Children.Add(Text("Now playing", 10, purple, true));
        labels.Children.Add(title);
        labels.Children.Add(artist);
        row.Children.Add(labels);
        body.Children.Add(row);
        var progress = Progress(purple);
        progress.Margin = new Thickness(0, 9, 0, 0);
        body.Children.Add(progress);
        var times = Text("", 10, muted);
        times.Margin = new Thickness(0, 4, 0, 0);
        if (expanded)
            body.Children.Add(times);
        body.Children.Add(transport);

        refresh = () =>
        {
            _ = RefreshPlaybackAsync();
            title.Text = playback?.Title ?? "Nothing playing";
            artist.Text = playback?.Artist ?? "Windows media";
            artwork.Source = playback?.Artwork;
            ((TextBlock)play.Content).Text = playback?.Playing == true ? "\uE769" : "\uE768";
            if (playback is { } current && current.Duration > TimeSpan.Zero)
            {
                var position = Math.Clamp(current.Position.TotalSeconds, 0, current.Duration.TotalSeconds);
                progress.Value = position / current.Duration.TotalSeconds * 100;
                times.Text = $"{FormatTime(TimeSpan.FromSeconds(position))}  /  {FormatTime(current.Duration)}";
            }
            else
            {
                progress.Value = 0;
                times.Text = "";
            }
        };
        return Card(body, purple);
    }

    private async Task RefreshPlaybackAsync()
    {
        if (readingMedia || DateTime.UtcNow < mediaRetryAfter)
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
            playback = new Playback(identity, item.Title, item.Artist, item.AlbumTitle, artwork, playing,
                timeline.Position - timeline.StartTime + elapsed, timeline.EndTime - timeline.StartTime);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            mediaInitialization = null;
            mediaManager = null;
            playback = null;
            mediaRetryAfter = DateTime.UtcNow.AddSeconds(15);
        }
        finally { readingMedia = false; }
    }

    private Border QuickCard()
    {
        var controls = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var details = ActionButton("All controls", () => ShowPage(DashboardPage.Controls));
        details.Padding = new Thickness(7, 4, 7, 4);
        controls.Children.Add(CardHeading("Quick controls", 0xE9E9, accent, details));
        var row = new WrapPanel();
        foreach (var (symbol, label, target) in new[]
        {
            (0xE701, "Wi-Fi", "ms-settings:network-wifi"),
            (0xE702, "Bluetooth", "ms-settings:bluetooth"),
            (0xEA8F, "Notifications", "ms-settings:notifications"),
            (0xE706, "Display", "ms-settings:display")
        })
        {
            var button = AccentButton(label, symbol, accent, () =>
            {
                if (label is "Wi-Fi" or "Bluetooth")
                    ShowPage(DashboardPage.Controls);
                else
                    OpenTarget(target);
            });
            button.Padding = new Thickness(6, 4, 6, 4);
            button.Margin = new Thickness(0, 0, 3, 3);
            row.Children.Add(button);
        }
        controls.Children.Add(row);
        var volumeRow = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        var speaker = DockGlyph(0xE767, 13, accent);
        speaker.Margin = new Thickness(0, 0, 7, 0);
        volumeRow.Children.Add(speaker);
        volumeRow.Children.Add(OutputSlider());
        controls.Children.Add(volumeRow);
        return Card(controls, accent);
    }

    private UIElement ControlsPage()
    {
        var grid = Columns(1, 1);
        foreach (var (index, label, symbol, tint, settings) in new[]
        {
            (0, "Network", 0xE701, accent, new[] { ("Wi-Fi networks", "network-wifi"), ("Ethernet", "network-ethernet"), ("Bluetooth devices", "bluetooth") }),
            (1, "Display", 0xE706, amber, new[] { ("Display", "display"), ("Night light", "nightlight"), ("Accessibility", "easeofaccess") }),
            (2, "Windows alerts", 0xEA8F, pink, new[] { ("Notification settings", "notifications"), ("Windows Focus", "quiethours") })
        })
        {
            var stack = new StackPanel();
            stack.Children.Add(CardHeading(label, symbol, tint));
            if (index == 0)
                stack.Children.Add(RadioControls());
            var links = new WrapPanel();
            foreach (var (name, path) in settings)
            {
                var button = ActionButton(name, path switch
                {
                    "network-wifi" => ShowWifiWidget,
                    "bluetooth" => ShowBluetoothWidget,
                    _ => () => OpenTarget("ms-settings:" + path)
                });
                button.Margin = new Thickness(0, 0, 4, 4);
                links.Children.Add(button);
            }
            stack.Children.Add(links);
            Put(grid, Card(stack, tint), index / 2, index % 2);
        }
        var sliders = new StackPanel();
        var mixer = AccentButton("App mixer", 0xE767, purple, ShowAudioWidget);
        mixer.Padding = new Thickness(7, 4, 7, 4);
        sliders.Children.Add(CardHeading("Output volume", 0xE767, purple, mixer));
        sliders.Children.Add(OutputSlider());
        var windowsMixer = ActionButton("Sound settings", () => OpenTarget("ms-settings:sound"));
        windowsMixer.HorizontalAlignment = HorizontalAlignment.Left;
        windowsMixer.Margin = new Thickness(0, 6, 0, 0);
        sliders.Children.Add(windowsMixer);
        Put(grid, Card(sliders, purple), 1, 1);
        return grid;
    }

    private UIElement RadioControls()
    {
        var body = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        var row = Columns(1, 1);
        var status = Text("", 11, AssistantSecondary);
        status.TextWrapping = TextWrapping.Wrap;
        body.Children.Add(row);
        body.Children.Add(status);
        foreach (var (kind, column) in new[] { (RadioKind.WiFi, 0), (RadioKind.Bluetooth, 1) })
        {
            var name = DockRadios.Label(kind);
            var toggle = ActionButton(name + " · Checking");
            toggle.IsEnabled = false;
            var on = false;
            toggle.Loaded += async (_, _) => await Refresh();
            toggle.Click += async (_, _) =>
            {
                toggle.IsEnabled = false;
                try { status.Text = await DockRadios.SetAsync(kind, !on, lifetime.Token); }
                catch (OperationCanceledException) { }
                catch (Exception error) when (DockActions.IsActionError(error)) { status.Text = error.Message; }
                finally { await Refresh(); }
            };
            Put(row, toggle, column);

            async Task Refresh()
            {
                var state = "unavailable";
                try { state = await DockRadios.StateAsync(kind, lifetime.Token); }
                catch (OperationCanceledException) { }
                catch (Exception error) when (DockActions.IsActionError(error)) { }
                on = state == "On";
                toggle.Content = name + " · " + state;
                toggle.IsEnabled = state is not ("unavailable" or "Disabled");
                AutomationProperties.SetName(toggle, $"{name}. {state}. Turn {(on ? "off" : "on")}");
            }
        }
        return body;
    }

    private Slider OutputSlider()
    {
        var slider = Range("Output volume", 100, 0);
        try
        {
            slider.Value = DockAudio.Output().Percent;
        }
        catch (COMException)
        {
            slider.IsEnabled = false;
            slider.ToolTip = "No active output device";
        }

        slider.ValueChanged += (_, e) =>
        {
            try
            {
                DockAudio.SetVolume(e.NewValue);
            }
            catch (COMException) { slider.IsEnabled = false; }
        };
        return slider;
    }

    private Border MixerCard()
    {
        var body = new StackPanel();
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var external = IconButton(0xE8A7, "Open Windows volume mixer", () => OpenTarget("ms-settings:apps-volume"));
        DockPanel.SetDock(external, Dock.Right);
        top.Children.Add(external);
        var refresh = IconButton(0xE72C, "Refresh audio devices and apps");
        DockPanel.SetDock(refresh, Dock.Right);
        top.Children.Add(refresh);
        var presets = ActionButton("Presets", ShowAudioPresets);
        presets.Padding = new Thickness(7, 3, 7, 3);
        presets.Margin = new Thickness(5, 0, 2, 0);
        DockPanel.SetDock(presets, Dock.Right);
        top.Children.Add(presets);
        top.Children.Add(IconLabel("Output and app mixer", 0xE9E9, accent));
        body.Children.Add(top);
        var outputLabel = Text("Default output", 10, subdued, true);
        outputLabel.Margin = new Thickness(0, 3, 0, 4);
        body.Children.Add(outputLabel);
        var outputs = new ComboBox { Margin = new Thickness(0, 0, 0, 6), DisplayMemberPath = nameof(AudioOutput.Name) };
        AutomationProperties.SetName(outputs, "Audio output");
        var status = Text("", 11, subdued);
        status.TextWrapping = TextWrapping.Wrap;
        var system = new DockPanel { Margin = new Thickness(0, 2, 0, 0) };
        var mute = ActionButton("Mute output");
        mute.Margin = new Thickness(6, 0, 0, 0);
        DockPanel.SetDock(mute, Dock.Right);
        system.Children.Add(mute);
        var volume = new ContentControl { VerticalAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        system.Children.Add(volume);
        body.Children.Add(outputs);
        body.Children.Add(system);
        body.Children.Add(status);
        var appHeading = Text("App volumes and outputs", 11, accent, true);
        appHeading.Margin = new Thickness(0, 8, 0, 0);
        body.Children.Add(appHeading);
        var sessions = new StackPanel();
        body.Children.Add(new ScrollViewer { Content = sessions, MaxHeight = 154, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 6, 0, 0) });
        AudioOutput[] devices = [];
        var populating = false;
        refresh.Click += (_, _) => Reload();
        mute.Click += (_, _) =>
        {
            try
            {
                AudioDevice(device =>
                {
                    using var endpoint = device.AudioEndpointVolume;
                    endpoint.Mute = !endpoint.Mute;
                });
                Reload();
            }
            catch (COMException) { status.Text = "The output is unavailable."; }
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
        return Card(body, accent);

        void Reload()
        {
            populating = true;
            sessions.Children.Clear();
            status.Text = "";
            volume.Content = OutputSlider();
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var list = new List<AudioOutput>();
                foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                {
                    using (device)
                        list.Add(new(device.ID, device.FriendlyName));
                }
                devices = list.ToArray();
                outputs.ItemsSource = devices;
                using var current = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                outputs.SelectedItem = devices.FirstOrDefault(item => item.Id == current.ID);
                using var endpoint = current.AudioEndpointVolume;
                mute.Content = endpoint.Mute ? "Unmute output" : "Mute output";
                AutomationProperties.SetName(mute, (string)mute.Content);
                foreach (var output in devices)
                {
                    using var device = enumerator.GetDevice(output.Id);
                    var active = device.AudioSessionManager.Sessions;
                    for (var index = 0; index < active.Count && sessions.Children.Count < 24; index++)
                    {
                        using var session = active[index];
                        if (session.State != NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateExpired)
                            AddSession(output, session);
                    }
                }
            }
            catch (COMException) { status.Text = "No active audio output."; }
            finally { populating = false; }
            outputs.IsEnabled = mute.IsEnabled = devices.Length > 0;
            if (sessions.Children.Count == 0)
                sessions.Children.Add(Text("No app audio sessions", 11, muted));
        }

        void AddSession(AudioOutput output, AudioSessionControl session)
        {
            var id = session.GetSessionInstanceIdentifier;
            var pid = session.GetProcessID;
            var name = session.IsSystemSoundsSession ? "System sounds" : AudioSessionName(session);
            var row = Columns(1.05, 1, 0.85);
            row.Margin = new Thickness(0, 3, 0, 0);
            var label = Text(name, 11, subdued, true);
            label.VerticalAlignment = VerticalAlignment.Center;
            label.ToolTip = output.Name;
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            var labels = new StackPanel();
            labels.Children.Add(label);
            var endpointLabel = Text(output.Name, 10, subdued);
            endpointLabel.TextTrimming = TextTrimming.CharacterEllipsis;
            endpointLabel.ToolTip = output.Name;
            labels.Children.Add(endpointLabel);
            Put(row, labels, 0);
            var level = Range(name + " volume", 100, session.SimpleAudioVolume.Volume * 100);
            level.ValueChanged += (_, e) =>
            {
                try
                {
                    using var enumerator = new MMDeviceEnumerator();
                    using var device = enumerator.GetDevice(output.Id);
                    var candidates = device.AudioSessionManager.Sessions;
                    for (var index = 0; index < candidates.Count; index++)
                    {
                        using var candidate = candidates[index];
                        if (candidate.GetSessionInstanceIdentifier != id)
                            continue;
                        candidate.SimpleAudioVolume.Volume = (float)(e.NewValue / 100);
                        break;
                    }
                }
                catch (COMException) { level.IsEnabled = false; }
            };
            Put(row, level, 1);
            if (pid != 0 && !session.IsSystemSoundsSession)
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
                try
                {
                    var route = AudioRouting.AppRoute(pid);
                    selected = route == null ? automatic : devices.FirstOrDefault(device => device.Id == route);
                }
                catch (Exception error) when (error is COMException or UnauthorizedAccessException)
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
                if (await action.AsTask(lifetime.Token))
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
        bool Playing, TimeSpan Position, TimeSpan Duration);
}
