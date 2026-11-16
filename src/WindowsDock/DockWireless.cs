using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace WindowsDock;

internal sealed record DockWifiNetwork(Guid Adapter, string Name, string Profile, int Signal,
    bool Connected, bool Connectable, bool Secured);

internal static class DockWifi
{
    internal static unsafe DockWifiNetwork[] Read(CancellationToken token)
    {
        using var client = Open();
        Check(WlanEnumInterfaces(client, 0, out var interfaces));
        var result = new List<DockWifiNetwork>();
        try
        {
            var count = Math.Clamp(Marshal.ReadInt32(interfaces), 0, 64);
            var adapters = (NativeAdapter*)(interfaces + 8);
            for (var i = 0; i < count; i++)
            {
                token.ThrowIfCancellationRequested();
                var adapter = adapters[i].Id;
                var error = WlanGetAvailableNetworkList(client, in adapter, 0, 0, out var networks);
                if (error == 0x80342002) // The adapter is powered off.
                    continue;
                Check(error);
                try
                {
                    var available = Math.Clamp(Marshal.ReadInt32(networks), 0, 1024);
                    var native = (NativeNetwork*)(networks + 8);
                    for (var j = 0; j < available; j++)
                    {
                        var item = native + j;
                        var name = Encoding.UTF8.GetString(new ReadOnlySpan<byte>(item->Ssid, (int)Math.Min(item->SsidLength, 32)));
                        name = string.Concat(name.Where(c => !char.IsControl(c)));
                        var profile = new ReadOnlySpan<char>(item->Profile, 256);
                        var limit = profile.IndexOf('\0');
                        var saved = (item->Flags & 2) != 0 && limit > 0 ? new string(profile[..limit]) : "";
                        if (name.Length == 0)
                            continue;
                        result.Add(new(adapter, name, saved, (int)Math.Min(item->Signal, 100),
                            (item->Flags & 1) != 0, item->Connectable != 0, item->Security != 0));
                    }
                }
                finally { WlanFreeMemory(networks); }
            }
        }
        finally { WlanFreeMemory(interfaces); }
        return result.OrderByDescending(network => network.Connected).ThenByDescending(network => network.Signal)
            .ThenBy(network => network.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    internal static void ChangeConnection(DockWifiNetwork network, bool connect)
    {
        using var client = Open();
        var adapter = network.Adapter;
        if (!connect)
        {
            Check(WlanDisconnect(client, in adapter, 0));
            return;
        }
        if (!network.Connectable || string.IsNullOrEmpty(network.Profile) || network.Profile.Length >= 256)
            throw new InvalidOperationException("Connect in Windows to create or update the saved network profile.");
        var parameters = new ConnectionParameters { Profile = network.Profile, BssType = 3 };
        Check(WlanConnect(client, in adapter, in parameters, 0));
    }

    private static WlanHandle Open()
    {
        Check(WlanOpenHandle(2, 0, out _, out var handle));
        return handle;
    }

    private static void Check(uint error)
    {
        if (error == 5)
            throw new UnauthorizedAccessException("Windows is blocking Wi-Fi access. Check Location permissions in Windows Settings.");
        if (error != 0)
            throw new Win32Exception(unchecked((int)error));
    }

    private sealed class WlanHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private WlanHandle() : base(true) { }
        protected override bool ReleaseHandle() => WlanCloseHandle(handle, 0) == 0;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private unsafe struct NativeAdapter
    {
        internal Guid Id;
        internal fixed char Description[256];
        internal uint State;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private unsafe struct NativeNetwork
    {
        internal fixed char Profile[256];
        internal uint SsidLength;
        internal fixed byte Ssid[32];
        internal uint BssType, BssidCount;
        internal int Connectable;
        internal uint Reason, PhyCount;
        internal fixed uint Phy[8];
        internal int MorePhy;
        internal uint Signal;
        internal int Security;
        internal uint Authentication, Cipher, Flags, Reserved;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ConnectionParameters
    {
        internal uint Mode;
        [MarshalAs(UnmanagedType.LPWStr)] internal string Profile;
        internal nint Ssid, DesiredBssids;
        internal uint BssType, Flags;
    }

    [DllImport("wlanapi.dll")] private static extern uint WlanOpenHandle(uint version, nint reserved, out uint negotiated, out WlanHandle handle);
    [DllImport("wlanapi.dll")] private static extern uint WlanCloseHandle(nint handle, nint reserved);
    [DllImport("wlanapi.dll")] private static extern uint WlanEnumInterfaces(WlanHandle handle, nint reserved, out nint interfaces);
    [DllImport("wlanapi.dll")] private static extern uint WlanGetAvailableNetworkList(WlanHandle handle, in Guid adapter, uint flags, nint reserved, out nint list);
    [DllImport("wlanapi.dll")] private static extern void WlanFreeMemory(nint buffer);
    [DllImport("wlanapi.dll")] private static extern uint WlanConnect(WlanHandle handle, in Guid adapter, in ConnectionParameters parameters, nint reserved);
    [DllImport("wlanapi.dll")] private static extern uint WlanDisconnect(WlanHandle handle, in Guid adapter, nint reserved);
}

internal sealed record DockBluetoothDevice(string Name, bool Connected, int? Battery);

internal static class DockBluetooth
{
    private const string Connected = "System.Devices.Aep.IsConnected";
    private const string Container = "System.Devices.Aep.ContainerId";
    private const string Battery = "System.Devices.BatteryLife";

    internal static async Task<DockBluetoothDevice[]> ReadAsync(CancellationToken token)
    {
        string[] fields = [Connected, Container, Battery];
        var results = await Task.WhenAll(
            DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true), fields).AsTask(token),
            DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true), fields).AsTask(token));
        return results.SelectMany(result => result).GroupBy(device =>
            device.Properties.TryGetValue(Container, out var group) && group is Guid id && id != Guid.Empty
                ? id.ToString() : device.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var name = group.FirstOrDefault(device => !string.IsNullOrEmpty(device.Name))?.Name ?? "Bluetooth device";
                var connected = group.Any(device => device.Properties.TryGetValue(Connected, out var status) && status is true);
                var battery = group.Select(device => device.Properties.TryGetValue(Battery, out var charge) ? charge switch
                {
                    byte value when value <= 100 => (int?)value,
                    uint value when value <= 100 => (int)value,
                    int value when value is >= 0 and <= 100 => value,
                    _ => null
                } : null).FirstOrDefault(charge => charge.HasValue);
                return new DockBluetoothDevice(name, connected, battery);
            })
            .OrderByDescending(device => device.Connected).ThenBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
}
