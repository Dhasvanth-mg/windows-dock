using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Radios;

namespace WindowsDock;

internal static class DockRadios
{
    internal static RadioKind Kind(string value) => value.ToLowerInvariant() switch
    {
        "wi-fi" or "wifi" => RadioKind.WiFi,
        "bluetooth" => RadioKind.Bluetooth,
        _ => throw new ArgumentException("Choose Wi-Fi or Bluetooth.")
    };

    internal static string Label(RadioKind kind) => kind == RadioKind.WiFi ? "Wi-Fi" : "Bluetooth";

    internal static async Task<string> StateAsync(RadioKind kind, CancellationToken token)
    {
        var radios = await GetAsync(kind, token);
        return radios.Length == 0 ? "unavailable" : string.Join(", ", radios.Select(radio => radio.State.ToString()).Distinct());
    }

    internal static async Task<string> SetAsync(RadioKind kind, bool enabled, CancellationToken token)
    {
        var name = Label(kind);
        var radios = await GetAsync(kind, token);
        if (radios.Length == 0)
            throw new InvalidOperationException($"Windows did not expose a {name} radio.");
        var desired = enabled ? RadioState.On : RadioState.Off;
        var changes = radios.Where(radio => radio.State != desired).ToArray();
        if (changes.Length == 0)
            return $"{name} is already {(enabled ? "on" : "off")}.";
        if (await Radio.RequestAccessAsync().AsTask(token) != RadioAccessStatus.Allowed)
            throw new InvalidOperationException($"Windows denied {name} control. Check its setting in Windows.");
        var denied = false;
        foreach (var radio in changes)
            denied |= await radio.SetStateAsync(desired).AsTask(token) != RadioAccessStatus.Allowed;
        for (var attempt = 0; attempt < 8 && radios.Any(radio => radio.State != desired); attempt++)
            await Task.Delay(150, token);
        var remaining = radios.Count(radio => radio.State != desired);
        if (denied || remaining != 0)
            throw new InvalidOperationException($"Windows could not confirm {(enabled ? "on" : "off")} for every {name} adapter. Check Windows Settings.");
        return $"{name} is {(enabled ? "on" : "off")}.";
    }

    private static async Task<Radio[]> GetAsync(RadioKind kind, CancellationToken token) =>
        (await Radio.GetRadiosAsync().AsTask(token)).Where(radio => radio.Kind == kind).ToArray();
}
