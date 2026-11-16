using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Ellipse = System.Windows.Shapes.Ellipse;
using ShapePath = System.Windows.Shapes.Path;
using System.Windows.Threading;

namespace WindowsDock;

public partial class DashboardWindow
{
    private readonly DispatcherTimer focusTicker = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTimeOffset? focusEnds;
    private DateTime focusStamp;
    private int focusMinutes = 25;
    private DateTime calendarWeek = DateTime.Today;
    private DateTime reminderDate = DateTime.Today;

    private Action? updateFocusWidget;

    private void ShowFocusWidget()
    {
        var (window, content, cancellation) = CreateDockPanel("Focus timer", "", 406, 0xE916, green, widget: true);
        BuildPinnedFocus(content, cancellation);
        window.Show();
    }

    private void BuildPinnedFocus(StackPanel content, System.Threading.CancellationToken cancellation)
    {
        RefreshFocusState();
        content.Children.Add(FocusCard(out var update));
        updateFocusWidget = update;
        cancellation.Register(() => updateFocusWidget = null);
        update();
    }

    private FrameworkElement FocusStrip(out Action update) => FocusCard(out update, compact: true);

    // Big numerals, a thin bar that drains, lengths as a segmented choice, one filled Start button.
    // Compact is the version on Today: the same controls in three short rows.
    private FrameworkElement FocusCard(out Action update, bool compact = false)
    {
        var focus = new StackPanel();
        var reading = Text("", compact ? 24 : 38, strong: true);
        Typography.SetNumeralAlignment(reading, FontNumeralAlignment.Tabular);
        var remaining = Progress(AccentFill);
        remaining.Height = 3;
        int[] lengths = [5, 10, 25, 50];
        var presets = Segmented("Timer length", lengths.Select(minutes => minutes + (compact ? "m" : " min")).ToArray(),
            Array.IndexOf(lengths, focusMinutes), index =>
            {
                focusMinutes = lengths[index];
                focusEnds = null;
                PersistFocus();
                UpdateDashboard();
            }, out var markPreset);
        var toggle = PrimaryButton("Start", () =>
        {
            focusEnds = focusEnds == null ? DateTimeOffset.Now.AddMinutes(focusMinutes) : null;
            PersistFocus();
            UpdateDashboard();
        });
        toggle.MinWidth = 64;
        toggle.VerticalAlignment = VerticalAlignment.Center;
        // On Today there is only room for an icon; the widget has space for the words.
        var quiet = compact ? IconButton(0xE708, "Do not disturb", () => OpenTarget("ms-settings:quiethours"))
            : QuietButton("Do not disturb in Windows", () => OpenTarget("ms-settings:quiethours"));
        quiet.ToolTip = "Opens Windows Focus, which silences notifications.";
        ToolTipService.SetIsEnabled(quiet, true);
        if (compact)
        {
            var top = new DockPanel { Margin = new Thickness(0, -4, 0, 4) };
            DockPanel.SetDock(toggle, Dock.Right);
            top.Children.Add(toggle);
            var caption = Text("Focus", 12, AssistantSecondary);
            caption.VerticalAlignment = VerticalAlignment.Bottom;
            caption.Margin = new Thickness(0, 0, 9, 4);
            top.Children.Add(caption);
            top.Children.Add(reading);
            focus.Children.Add(top);
            remaining.Margin = new Thickness(0, 0, 0, 8);
            focus.Children.Add(remaining);
            var bottom = new DockPanel();
            quiet.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(quiet, Dock.Right);
            bottom.Children.Add(quiet);
            bottom.Children.Add(presets);
            focus.Children.Add(bottom);
        }
        else
        {
            reading.Margin = new Thickness(0, -6, 0, 4);
            focus.Children.Add(reading);
            remaining.Margin = new Thickness(0, 0, 0, 12);
            focus.Children.Add(remaining);
            var actions = new DockPanel();
            toggle.Margin = new Thickness(10, 0, 0, 0);
            DockPanel.SetDock(toggle, Dock.Right);
            actions.Children.Add(toggle);
            actions.Children.Add(presets);
            focus.Children.Add(actions);
            quiet.HorizontalAlignment = HorizontalAlignment.Left;
            quiet.Margin = new Thickness(0, 10, 0, 0);
            focus.Children.Add(quiet);
        }
        bool? showingTimer = null;
        var displayedPreset = 0;
        var displayedSeconds = -1;
        update = () =>
        {
            var running = focusEnds.HasValue;
            var duration = TimeSpan.FromMinutes(focusMinutes);
            var seconds = (int)Math.Ceiling(Math.Clamp((focusEnds - DateTimeOffset.Now ?? duration).TotalSeconds, 0, duration.TotalSeconds));
            if (displayedSeconds != seconds || displayedPreset != focusMinutes || showingTimer != running)
            {
                displayedSeconds = seconds;
                reading.Text = FormatTime(TimeSpan.FromSeconds(seconds));
                // Empty while idle, so a full bar always means a session that has just started.
                remaining.Value = running ? seconds / duration.TotalSeconds * 100 : 0;
            }
            if (showingTimer != running)
            {
                showingTimer = running;
                var label = running ? "Reset" : "Start";
                toggle.Content = label;
                MarkPrimary(toggle, !running);
                System.Windows.Automation.AutomationProperties.SetName(toggle, label);
            }
            if (displayedPreset != focusMinutes)
            {
                displayedPreset = focusMinutes;
                markPreset(Array.IndexOf(lengths, focusMinutes));
            }
        };
        return focus;
    }

    private void InitializeFocus()
    {
        RefreshFocusState();
        focusTicker.Tick += (_, _) =>
        {
            RefreshFocusState();
            CheckFocusTimer();
            updateFocusWidget?.Invoke();
        };
        if (focusEnds.HasValue)
            focusTicker.Start();
        Closed += (_, _) => focusTicker.Stop();
    }

    private void RefreshFocusState()
    {
        DateTime stamp;
        try { stamp = File.GetLastWriteTimeUtc(DataPath("focus.json")); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return; }
        if (focusStamp == stamp)
            return;
        focusStamp = stamp;
        var saved = ReadJson<FocusState>("focus.json");
        focusMinutes = saved is { Minutes: >= 1 and <= 120 } ? saved.Minutes : 25;
        focusEnds = saved?.Ends > DateTimeOffset.Now ? saved.Ends : null;
        if (focusEnds.HasValue)
            focusTicker.Start();
        else
            focusTicker.Stop();
    }

    private void CheckFocusTimer()
    {
        if (!focusEnds.HasValue || focusEnds > DateTimeOffset.Now)
            return;
        focusEnds = null;
        if (releaseWhenIdle)
            focusTicker.Stop();
        else
        {
            PersistFocus();
            System.Media.SystemSounds.Asterisk.Play();
        }
        if (IsVisible)
            UpdateDashboard();
    }

    private void PersistFocus()
    {
        if (focusEnds.HasValue)
            focusTicker.Start();
        else
            focusTicker.Stop();
        WriteJson("focus.json", new FocusState(focusMinutes, focusEnds));
    }

    private static string DataPath(string file) => DockData.FilePath(file);

    private static T? ReadJson<T>(string file) => DockData.Read<T>(file);

    private void WriteJson<T>(string file, T value)
    {
        try
        {
            DockData.Save(file, value);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            ShowOwnedMessage("Windows Dock could not save this change.");
        }
    }

}
