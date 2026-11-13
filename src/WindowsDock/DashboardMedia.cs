using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NAudio.CoreAudioApi;
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

    private UIElement HomePage()
    {
        var grid = Columns(2.1, 1.5, 0.85, 1.0, 1.2);
        grid.MinHeight = 102;
        var media = MediaCard(false, out var updateMedia);
        Put(grid, media, 0);
        Put(grid, QuickCard(), 1);

        var date = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var today = DateTime.Today;
        var dateLabel = Text(today.ToString("ddd MMM"), 11, amber, true);
        var dateNumber = Text(today.ToString("dd"), 30, strong: true);
        dateLabel.TextAlignment = dateNumber.TextAlignment = TextAlignment.Center;
        date.Children.Add(dateLabel);
        date.Children.Add(dateNumber);
        var calendar = ActionButton("Calendar", () => ShowPage(DashboardPage.Calendar));
        calendar.Padding = new Thickness(6, 3, 6, 3);
        calendar.HorizontalAlignment = HorizontalAlignment.Center;
        date.Children.Add(calendar);
        Put(grid, Card(date), 2);

        var forecast = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        forecast.Children.Add(Text("Weather", 11, muted, true));
        var temperature = Text(weather == null ? "—" : $"{weather.Temperature:0}°", 27, strong: true);
        forecast.Children.Add(temperature);
        var weatherLink = ActionButton(weather?.City ?? "Choose city", () => ShowPage(DashboardPage.Weather));
        weatherLink.Padding = new Thickness(6, 3, 6, 3);
        weatherLink.HorizontalAlignment = HorizontalAlignment.Left;
        forecast.Children.Add(weatherLink);
        Put(grid, Card(forecast), 3);

        var overview = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var cpu = MetricBar(overview, "CPU", green);
        var memory = MetricBar(overview, "Memory", accent);
        var details = ActionButton("System stats", () => ShowPage(DashboardPage.Stats));
        details.Padding = new Thickness(6, 3, 6, 3);
        details.HorizontalAlignment = HorizontalAlignment.Left;
        details.Margin = new Thickness(0, 4, 0, 0);
        overview.Children.Add(details);
        Put(grid, Card(overview), 4);
        updatePage = () =>
        {
            updateMedia();
            var current = DateTime.Today;
            dateLabel.Text = current.ToString("ddd MMM");
            dateNumber.Text = current.ToString("dd");
            temperature.Text = weather == null ? "—" : $"{weather.Temperature:0}°";
            var snapshot = ReadMetrics();
            cpu(snapshot.Cpu, $"{snapshot.Cpu:0}%");
            memory(snapshot.Memory, $"{snapshot.Memory:0}%");
        };
        return grid;
    }

    private UIElement MediaPage()
    {
        var grid = Columns(1.35, 1);
        var card = MediaCard(true, out var update);
        Put(grid, card, 0);
        if (lyricsOpen)
        {
            Put(grid, LyricsCard(out var refreshLyrics), 1);
            updatePage = () => { update(); refreshLyrics(); };
        }
        else
        {
            Put(grid, MixerCard(), 1);
            updatePage = update;
        }
        return grid;
    }

    private Border MediaCard(bool expanded, out Action refresh)
    {
        var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var row = new DockPanel { LastChildFill = true };
        var artSize = expanded ? 67 : 50;
        var artwork = new Image { Width = artSize, Height = artSize, Stretch = Stretch.UniformToFill };
        var icon = new TextBlock { Text = "\uE8D6", FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 23, Foreground = subdued, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center };
        var art = new Grid();
        art.Children.Add(icon);
        art.Children.Add(artwork);
        var artworkFrame = new Border { Width = artSize, Height = artSize, Background = Brush("#343945"),
            CornerRadius = new CornerRadius(10), Child = art, Margin = new Thickness(0, 0, 9, 0),
            Clip = new RectangleGeometry(new Rect(0, 0, artSize, artSize), 10, 10) };
        row.Children.Add(artworkFrame);
        var transport = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(transport, Dock.Right);
        transport.Children.Add(IconButton(0xE892, "Previous track", () => ControlPlayback(0xB1)));
        var play = IconButton(0xE768, "Play or pause", () => ControlPlayback(0xB3));
        play.Background = Brushes.White;
        play.Foreground = Brush("#171A21");
        transport.Children.Add(play);
        transport.Children.Add(IconButton(0xE893, "Next track", () => ControlPlayback(0xB0)));
        if (expanded)
        {
            var lyrics = IconButton(lyricsOpen ? 0xE9E9 : 0xE8A5, lyricsOpen ? "Show mixer" : "Find synced lyrics with LRCLIB", () =>
            {
                lyricsOpen = !lyricsOpen;
                lyricError = null;
                ShowPage(DashboardPage.Media);
            });
            transport.Children.Add(lyrics);
        }
        row.Children.Add(transport);
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) };
        var title = Text("Nothing playing", expanded ? 21 : 13, strong: true);
        var artist = Text("Windows media", 11, subdued);
        labels.Children.Add(title);
        labels.Children.Add(artist);
        row.Children.Add(labels);
        body.Children.Add(row);
        var progress = new ProgressBar { Height = 3, Maximum = 100, Foreground = Brushes.White, Background = Brush("#30FFFFFF"),
            BorderThickness = new Thickness(0), Margin = new Thickness(0, 9, 0, 0) };
        body.Children.Add(progress);
        var times = Text("", 10, muted);
        times.Margin = new Thickness(0, 4, 0, 0);
        if (expanded)
            body.Children.Add(times);

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
        return Card(body);
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
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var (symbol, label, target) in new[]
        {
            (0xE701, "Wi-Fi", "ms-settings:network-wifi"),
            (0xE702, "Bluetooth", "ms-settings:bluetooth"),
            (0xE7F4, "Notifications", "ms-settings:notifications"),
            (0xE706, "Display", "ms-settings:display")
        })
        {
            var button = IconButton(symbol, label, () => OpenTarget(target));
            button.Background = label == "Display" ? Brush("#32FFFFFF") : Brush("#215C9D");
            row.Children.Add(button);
        }
        controls.Children.Add(row);
        var slider = OutputSlider();
        slider.Margin = new Thickness(0, 8, 0, 0);
        controls.Children.Add(slider);
        controls.Children.Add(Text("Output volume", 10, subdued));
        return Card(controls);
    }

    private UIElement ControlsPage()
    {
        var grid = Columns(1, 1, 1, 1, 1.65);
        foreach (var (index, label, detail, second, secondDetail) in new[]
        {
            (0, "Wi-Fi", "ms-settings:network-wifi", "Ethernet", "ms-settings:network-ethernet"),
            (1, "Bluetooth", "ms-settings:bluetooth", "Display", "ms-settings:display"),
            (2, "Notifications", "ms-settings:notifications", "Night light", "ms-settings:nightlight"),
            (3, "Sound", "ms-settings:sound", "Accessibility", "ms-settings:easeofaccess")
        })
        {
            var stack = new StackPanel();
            var first = ActionButton(label, () => OpenTarget(detail));
            first.Margin = new Thickness(0, 0, 0, 6);
            stack.Children.Add(first);
            stack.Children.Add(ActionButton(second, () => OpenTarget(secondDetail)));
            Put(grid, Card(stack), index);
        }
        var sliders = new StackPanel();
        sliders.Children.Add(Text("Output volume", 11, subdued, true));
        sliders.Children.Add(OutputSlider());
        sliders.Children.Add(ActionButton("Windows volume mixer", () => OpenTarget("ms-settings:apps-volume")));
        Put(grid, Card(sliders), 4);
        return grid;
    }

    private Slider OutputSlider()
    {
        var slider = Range("Output volume", 100, 0);
        try
        {
            AudioDevice(device =>
            {
                using var endpoint = device.AudioEndpointVolume;
                slider.Value = endpoint.MasterVolumeLevelScalar * 100;
            });
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
                AudioDevice(device =>
                {
                    using var endpoint = device.AudioEndpointVolume;
                    endpoint.MasterVolumeLevelScalar = (float)(e.NewValue / 100);
                });
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
        top.Children.Add(Text("Volume mixer", 13, strong: true));
        body.Children.Add(top);
        body.Children.Add(OutputSlider());
        var sessions = new StackPanel();
        body.Children.Add(new ScrollViewer { Content = sessions, MaxHeight = 110, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        try
        {
            AudioDevice(device =>
            {
                var active = device.AudioSessionManager.Sessions;
                for (var index = 0; index < active.Count; index++)
                {
                    using var session = active[index];
                    if (session.State == NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateExpired)
                        continue;
                    var id = session.GetSessionInstanceIdentifier;
                    var name = session.IsSystemSoundsSession ? "System sounds" : AudioSessionName(session);
                    var row = Columns(1, 1.2);
                    row.Margin = new Thickness(0, 3, 0, 0);
                    Put(row, Text(name, 11, subdued), 0);
                    var level = Range(name + " volume", 100, session.SimpleAudioVolume.Volume * 100);
                    level.ValueChanged += (_, e) =>
                    {
                        try
                        {
                            AudioDevice(current =>
                            {
                                var candidates = current.AudioSessionManager.Sessions;
                                for (var i = 0; i < candidates.Count; i++)
                                {
                                    using var candidate = candidates[i];
                                    if (candidate.GetSessionInstanceIdentifier != id)
                                        continue;
                                    candidate.SimpleAudioVolume.Volume = (float)(e.NewValue / 100);
                                    break;
                                }
                            });
                        }
                        catch (COMException) { level.IsEnabled = false; }
                    };
                    Put(row, level, 1);
                    sessions.Children.Add(row);
                }
            });
        }
        catch (COMException) { }
        if (sessions.Children.Count == 0)
            sessions.Children.Add(Text("No app audio sessions", 11, muted));
        return Card(body);
    }

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
