using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace WindowsDock;

internal static class AudioRouting
{
    private const string EndpointPrefix = @"\\?\SWD#MMDEVAPI#";
    private const string RenderSuffix = "#{e6327cad-dcec-4949-ae8a-991e976a79d2}";

    // Audio policy's COM interfaces are private to Windows. Keep calls isolated so the mixer
    // can still use the public Core Audio APIs if a later Windows build changes them.
    internal static void SetDefault(string id)
    {
        var client = new PolicyConfig();
        try
        {
            var policy = (IDefaultEndpoint)client;
            foreach (var role in new[] { Role.Multimedia, Role.Console })
                Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(id, role));
        }
        finally { Marshal.ReleaseComObject(client); }
    }

    internal static void RouteApp(uint pid, string? id)
    {
        using var type = new HString("Windows.Media.Internal.AudioPolicyConfig");
        using var endpoint = new HString(id == null ? null : EndpointPrefix + id + RenderSuffix);
        var iid = new Guid("AB3D4648-E242-459F-B02F-541C70306324");
        Marshal.ThrowExceptionForHR(RoGetActivationFactory(type.Handle, in iid, out var factory));
        try
        {
            unsafe
            {
                // IInspectable contributes six entries. SetPersistedDefaultAudioEndpoint follows 19 factory methods.
                var vtable = *(nint**)factory;
                var setEndpoint = (delegate* unmanaged[Stdcall]<nint, uint, int, int, nint, int>)vtable[25];
                foreach (var role in new[] { Role.Multimedia, Role.Console })
                    Marshal.ThrowExceptionForHR(setEndpoint(factory, pid, (int)DataFlow.Render, (int)role, endpoint.Handle));
            }
        }
        finally { Marshal.Release(factory); }
    }

    internal static string? AppRoute(uint pid)
    {
        using var type = new HString("Windows.Media.Internal.AudioPolicyConfig");
        var iid = new Guid("AB3D4648-E242-459F-B02F-541C70306324");
        Marshal.ThrowExceptionForHR(RoGetActivationFactory(type.Handle, in iid, out var factory));
        try
        {
            unsafe
            {
                var vtable = *(nint**)factory;
                var getEndpoint = (delegate* unmanaged[Stdcall]<nint, uint, int, int, nint*, int>)vtable[26];
                nint endpoint = 0;
                try
                {
                    var result = getEndpoint(factory, pid, (int)DataFlow.Render, (int)Role.Multimedia, &endpoint);
                    if (result < 0 || endpoint == 0)
                        return null;
                    var pointer = WindowsGetStringRawBuffer(endpoint, out var length);
                    var device = pointer != 0 && length is > 0 and < 1024 ? Marshal.PtrToStringUni(pointer, (int)length) : null;
                    return device != null && device.StartsWith(EndpointPrefix, StringComparison.OrdinalIgnoreCase) &&
                        device.EndsWith(RenderSuffix, StringComparison.OrdinalIgnoreCase)
                        ? device[EndpointPrefix.Length..^RenderSuffix.Length] : null;
                }
                finally { if (endpoint != 0) WindowsDeleteString(endpoint); }
            }
        }
        finally { Marshal.Release(factory); }
    }

    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    private class PolicyConfig { }

    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDefaultEndpoint
    {
        void _VtblGap1_10();
        [PreserveSig]
        int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string device, Role role);
    }

    private sealed class HString : IDisposable
    {
        public nint Handle { get; }

        public HString(string? text)
        {
            if (text == null)
                return;
            Marshal.ThrowExceptionForHR(WindowsCreateString(text, (uint)text.Length, out var handle));
            Handle = handle;
        }

        public void Dispose()
        {
            if (Handle != 0)
                WindowsDeleteString(Handle);
        }
    }

    [DllImport("combase", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string source, uint length, out nint value);

    [DllImport("combase", ExactSpelling = true)]
    private static extern int WindowsDeleteString(nint value);

    [DllImport("combase", ExactSpelling = true)]
    private static extern nint WindowsGetStringRawBuffer(nint value, out uint length);

    [DllImport("combase", ExactSpelling = true)]
    private static extern int RoGetActivationFactory(nint name, in Guid iid, out nint result);
}
