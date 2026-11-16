using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Windows.Devices.Radios;

namespace WindowsDock;

public partial class DashboardWindow
{
    private void ShowWifiWidget()
    {
        var (window, content, cancellation) = CreateDockPanel("Wi-Fi", "", 470, 0xE701, accent, widget: true);
        BuildWireless(content, RadioKind.WiFi, cancellation);
        window.Show();
    }

    private void ShowBluetoothWidget()
    {
        var (window, content, cancellation) = CreateDockPanel("Bluetooth", "", 470, 0xE702, accent, widget: true);
        BuildWireless(content, RadioKind.Bluetooth, cancellation);
        window.Show();
    }

    private void BuildWireless(StackPanel content, RadioKind kind, CancellationToken cancellation)
    {
        var wifi = kind == RadioKind.WiFi;
        var name = DockRadios.Label(kind);
        var settings = wifi ? "ms-settings:network-wifi" : "ms-settings:bluetooth";
        var controls = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        var state = Text("", 12, AssistantSecondary);
        var status = Text("", 12, AssistantSecondary);
        status.TextWrapping = TextWrapping.Wrap;
        var list = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        var turnOn = ActionButton("Turn on");
        var turnOff = ActionButton("Turn off");
        var refresh = ActionButton("Refresh");
        foreach (var button in new[] { turnOn, turnOff, refresh,
            ActionButton("Windows settings", () => OpenTarget(settings)) })
        {
            button.Margin = new Thickness(0, 0, 5, 3);
            controls.Children.Add(button);
        }
        var location = ActionButton("Location permissions", () => OpenTarget("ms-settings:privacy-location"));
        turnOn.Click += (_, _) => _ = SetRadioAsync(true);
        turnOff.Click += (_, _) => _ = SetRadioAsync(false);
        refresh.Click += (_, _) => _ = RunAsync(LoadAsync);
        location.HorizontalAlignment = HorizontalAlignment.Left;
        location.Visibility = Visibility.Collapsed;
        content.Children.Add(controls);
        content.Children.Add(state);
        content.Children.Add(status);
        content.Children.Add(location);
        content.Children.Add(list);
        _ = RunAsync(LoadAsync);

        async Task RunAsync(Func<Task> action)
        {
            if (!refresh.IsEnabled || cancellation.IsCancellationRequested)
                return;
            refresh.IsEnabled = turnOn.IsEnabled = turnOff.IsEnabled = list.IsEnabled = false;
            location.Visibility = Visibility.Collapsed;
            status.Text = "Reading " + name + "…";
            try { await action(); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception error) when (error is InvalidOperationException or UnauthorizedAccessException or Win32Exception or COMException)
            {
                if (!cancellation.IsCancellationRequested)
                {
                    list.Children.Clear();
                    status.Text = error.Message;
                    if (wifi && error is UnauthorizedAccessException)
                        location.Visibility = Visibility.Visible;
                }
            }
            finally
            {
                if (!cancellation.IsCancellationRequested)
                    refresh.IsEnabled = turnOn.IsEnabled = turnOff.IsEnabled = list.IsEnabled = true;
            }
        }

        async Task LoadAsync()
        {
            state.Text = $"{name} · {await DockRadios.StateAsync(kind, cancellation)}";
            if (wifi)
                RenderNetworks(await Task.Run(() => DockWifi.Read(cancellation), cancellation));
            else
                RenderDevices(await DockBluetooth.ReadAsync(cancellation));
            cancellation.ThrowIfCancellationRequested();
            status.Text = "";
        }

        Task SetRadioAsync(bool on) => RunAsync(async () =>
        {
            var message = await DockRadios.SetAsync(kind, on, cancellation);
            await LoadAsync();
            status.Text = message;
        });

        void RenderNetworks(DockWifiNetwork[] networks)
        {
            list.Children.Clear();
            if (networks.Length == 0)
                list.Children.Add(Text("Windows reported no available networks.", 12, AssistantSecondary));
            foreach (var network in networks.Take(35))
            {
                var detail = network.Connected ? $"Connected · {network.Signal}% signal" :
                    $"{network.Signal}% signal · {(network.Secured ? "Secured" : "Open")}";
                var known = network.Connectable && network.Profile.Length > 0;
                var action = network.Connected
                    ? ActionButton("Disconnect", () => _ = ChangeNetworkAsync(network, false))
                    : known ? ActionButton("Connect", () => _ = ChangeNetworkAsync(network, true))
                    : ActionButton("Open in Windows", () => OpenTarget(settings));
                AddRow(network.Name, detail, action);
            }
        }

        Task ChangeNetworkAsync(DockWifiNetwork network, bool connect) => RunAsync(async () =>
        {
            await Task.Run(() => DockWifi.ChangeConnection(network, connect), cancellation);
            DockWifiNetwork[] results = [];
            var confirmed = false;
            for (var attempt = 0; attempt < 8; attempt++)
            {
                await Task.Delay(350, cancellation);
                results = await Task.Run(() => DockWifi.Read(cancellation), cancellation);
                confirmed = results.Any(item => item.Adapter == network.Adapter && item.Name == network.Name &&
                    item.Connected && (!connect || item.Profile == network.Profile)) == connect;
                if (confirmed)
                    break;
            }
            RenderNetworks(results);
            status.Text = confirmed ? (connect ? $"Connected to {network.Name}." : $"Disconnected from {network.Name}.")
                : "Windows has not confirmed the connection change. Use Refresh to check again.";
        });

        void RenderDevices(DockBluetoothDevice[] devices)
        {
            list.Children.Clear();
            if (devices.Length == 0)
                list.Children.Add(Text("Windows reported no paired Bluetooth devices.", 12, AssistantSecondary));
            foreach (var device in devices.Take(35))
            {
                var detail = device.Connected ? "Connected" : "Paired";
                if (device.Battery is { } battery)
                    detail += $" · {battery}% battery";
                AddRow(device.Name, detail, null);
            }
        }

        void AddRow(string title, string detail, Button? action)
        {
            var line = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            if (action != null)
            {
                action.Margin = new Thickness(6, 0, 0, 0);
                DockPanel.SetDock(action, Dock.Right);
                line.Children.Add(action);
            }
            var label = Text(title, 13, AssistantInk, true);
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            line.Children.Add(new StackPanel { Children = { label, Text(detail, 11, AssistantSecondary) } });
            list.Children.Add(line);
        }
    }
}
