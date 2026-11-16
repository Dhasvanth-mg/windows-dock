using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Radios;

namespace WindowsDock;

internal sealed record DockAction(string Name, string Preview, Func<CancellationToken, Task<string>> Execute);

internal static class DockActions
{
    internal const string ArgumentsGuide = """
        set_volume {percent: integer 0 to 100}
        set_mute {muted: boolean}
        set_app_volume {app: exact app name from dock_status, percent: integer 0 to 100}
        set_audio_output {name: exact output name from dock_status}
        apply_audio_preset {name: exact preset name from dock_status}
        playback {command: "play", "pause", "next", or "previous"}
        add_reminder {title: string, date: "YYYY-MM-DD"}
        edit_reminder {title: exact old title, date: old "YYYY-MM-DD", new_title: string, new_date: "YYYY-MM-DD"}
        complete_reminder {title: exact title, date: "YYYY-MM-DD"}
        start_timer {minutes: integer 1 to 120}
        stop_timer {}
        set_radio {radio: "wifi" or "bluetooth", enabled: boolean}
        set_startup {enabled: boolean}
        pin_widget {widget: widget ID}
        favorite_widget {widget: widget ID, enabled: boolean}
        mark_activity_read {}
        Widget IDs: audio, audio-presets, meetings, clipboard, github, workspaces, downloads, focus, notifications, assistant, activity, jobs.
        """;

    internal static DockAction Plan(string name, JsonElement args)
    {
        if (args.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Dock action arguments must be an object.");
        switch (name)
        {
            case "set_volume":
                var percent = Integer(args, "percent", 0, 100);
                return Local(name, $"Set output volume to {percent}%", () => $"Output volume is {DockAudio.SetVolume(percent)}%.");
            case "set_mute":
                var muted = Boolean(args, "muted");
                return Local(name, muted ? "Mute audio output" : "Unmute audio output", () =>
                    DockAudio.SetMute(muted) ? "Audio output is muted." : "Audio output is unmuted.");
            case "set_app_volume":
                var app = String(args, "app", 120);
                var appVolume = Integer(args, "percent", 0, 100);
                return Local(name, $"Set {app} volume to {appVolume}%", () =>
                {
                    AudioPresets.ChangeAppVolume(app, appVolume);
                    return $"Set {app} volume to {appVolume}%.";
                });
            case "set_audio_output":
                var output = String(args, "name", 256);
                return Local(name, $"Switch audio output to {output}", () => $"Default audio output is {DockAudio.SetOutput(output)}.");
            case "apply_audio_preset":
                var preset = String(args, "name", 40);
                return Local(name, $"Apply audio preset {preset}", () => AudioPresets.Apply(Presets().FirstOrDefault(
                    item => item.Name.Equals(preset, StringComparison.OrdinalIgnoreCase)) ??
                    throw new InvalidOperationException("That audio preset could not be found.")));
            case "playback":
                var media = String(args, "command", 8);
                if (media is not ("play" or "pause" or "next" or "previous"))
                    throw new ArgumentException("Choose play, pause, next, or previous.");
                return new(name, $"{char.ToUpperInvariant(media[0])}{media[1..]} in the current media session", token => DockAudio.PlaybackAsync(media, token));
            case "add_reminder":
                var added = ReadReminder(args, "title", "date");
                return Local(name, $"Add reminder “{added.Title}” for {added.Date:d}", () =>
                {
                    DockReminders.Add(added);
                    return $"“{added.Title}” is in Reminders for {added.Date:d}.";
                });
            case "edit_reminder":
                var before = ReadReminder(args, "title", "date");
                var after = ReadReminder(args, "new_title", "new_date");
                return Local(name, $"Change “{before.Title}” ({before.Date:d}) to “{after.Title}” ({after.Date:d})", () =>
                {
                    DockReminders.Change(before, after);
                    return $"Reminder updated to “{after.Title}” for {after.Date:d}.";
                });
            case "complete_reminder":
                var done = ReadReminder(args, "title", "date");
                return Local(name, $"Complete reminder “{done.Title}” ({done.Date:d})", () =>
                {
                    DockReminders.Change(done, null);
                    return $"Completed “{done.Title}”.";
                });
            case "start_timer":
                var minutes = Integer(args, "minutes", 1, 120);
                return Local(name, $"Start a {minutes}-minute focus timer", () =>
                {
                    DockData.Update<FocusState>("focus.json", _ => new(minutes, DateTimeOffset.Now.AddMinutes(minutes)));
                    return $"Started a {minutes}-minute focus timer.";
                });
            case "stop_timer":
                return Local(name, "Stop the focus timer", () =>
                {
                    DockData.Update<FocusState>("focus.json", state => new(state?.Minutes ?? 25, null));
                    return "Focus timer stopped.";
                });
            case "set_radio":
                var kind = DockRadios.Kind(String(args, "radio", 16));
                var radioEnabled = Boolean(args, "enabled");
                return new(name, $"Turn {(radioEnabled ? "on" : "off")} {DockRadios.Label(kind)}",
                    token => DockRadios.SetAsync(kind, radioEnabled, token));
            case "set_startup":
                var startup = Boolean(args, "enabled");
                return Local(name, startup ? "Start Windows Dock at Windows sign-in" : "Remove Windows Dock from sign-in startup", () =>
                {
                    DockStartup.Set(startup);
                    return startup ? "Windows Dock is registered to start at sign-in." : "Windows Dock was removed from sign-in startup.";
                });
            case "pin_widget":
                var pin = DockWidgetLayout.WidgetId(String(args, "widget", 32));
                return Local(name, $"Pin {pin} above favorites", () =>
                {
                    DockWidgetLayout.Pin(pin);
                    return $"Pinned {pin} at the top of Widgets.";
                });
            case "favorite_widget":
                var favorite = DockWidgetLayout.WidgetId(String(args, "widget", 32));
                var enabled = Boolean(args, "enabled");
                return Local(name, enabled ? $"Add {favorite} to favorites" : $"Remove {favorite} from favorites", () =>
                {
                    var layout = DockWidgetLayout.SetFavorite(favorite, enabled);
                    if (enabled && !layout.Favorites.Contains(favorite))
                        throw new InvalidOperationException(layout.Pinned == favorite
                            ? "That widget is already pinned." : "All four favorite slots are filled. Remove one first.");
                    return enabled ? $"Added {favorite} to favorites." : $"Removed {favorite} from favorites.";
                });
            case "mark_activity_read":
                return Local(name, "Mark the activity inbox read", () =>
                {
                    var last = DockData.Read<InboxItem[]>("activity-inbox.json")?.FirstOrDefault();
                    if (last == null)
                        return "The activity inbox is empty.";
                    DockData.Update<DateTimeOffset>("activity-seen.json", seen => last.At > seen ? last.At : seen);
                    return "Marked the activity inbox read.";
                });
            default:
                throw new ArgumentException("That tool is unavailable in Windows Dock.");
        }
    }

    internal static async Task<string> StatusAsync(CancellationToken token)
    {
        var layout = DockWidgetLayout.Load();
        var timer = DockData.Read<FocusState>("focus.json");
        (int Percent, bool Muted)? output = null;
        string[] outputs = [];
        string[] apps = [];
        try { output = DockAudio.Output(); outputs = DockAudio.Outputs(); apps = AudioPresets.ActiveApps(); }
        catch (Exception error) when (error is COMException or InvalidOperationException) { }
        return JsonSerializer.Serialize(new
        {
            volume = output?.Percent,
            muted = output?.Muted,
            outputs,
            apps,
            audioPresets = Presets().Select(item => item.Name),
            wifi = await ReadRadioAsync(RadioKind.WiFi, token),
            bluetooth = await ReadRadioAsync(RadioKind.Bluetooth, token),
            startup = DockStartup.Enabled(),
            focusEnds = timer?.Ends > DateTimeOffset.Now ? timer.Ends : null,
            layout
        });
    }

    internal static string String(JsonElement args, string key, int limit) =>
        args.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String &&
        value.GetString()?.Trim() is { Length: > 0 } text && text.Length <= limit && !text.Any(char.IsControl)
            ? text : throw new ArgumentException($"Enter a value for {key} of up to {limit} characters.");

    internal static bool IsActionError(Exception error) =>
        error is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or
        COMException or System.Security.SecurityException or System.ComponentModel.Win32Exception or
        System.Net.Http.HttpRequestException or ModelContextProtocol.McpException or JsonException;

    private static async Task<string> ReadRadioAsync(RadioKind kind, CancellationToken token)
    {
        try { return await DockRadios.StateAsync(kind, token); }
        catch (Exception error) when (IsActionError(error)) { return "unavailable"; }
    }

    private static Reminder ReadReminder(JsonElement args, string titleKey, string dateKey)
    {
        var title = String(args, titleKey, 160);
        var value = String(args, dateKey, 10);
        if (!DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new ArgumentException($"Enter {dateKey} as YYYY-MM-DD.");
        return new(title, date.Date);
    }

    private static bool Boolean(JsonElement args, string key) =>
        args.TryGetProperty(key, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean() : throw new ArgumentException($"Set {key} to true or false.");

    private static int Integer(JsonElement args, string key, int minimum, int maximum) =>
        args.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var number) && number >= minimum && number <= maximum
            ? number : throw new ArgumentException($"Set {key} to a whole number from {minimum} to {maximum}.");

    private static IEnumerable<AudioPreset> Presets() => (DockData.Read<AudioPreset[]>("audio-presets.json") ?? [])
        .Where(item => item is { Name.Length: > 0, DeviceId.Length: > 0, Apps: not null });

    private static DockAction Local(string name, string preview, Func<string> apply) => new(name, preview, token =>
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult(apply());
    });
}
