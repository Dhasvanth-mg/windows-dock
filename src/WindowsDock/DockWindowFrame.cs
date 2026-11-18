using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WindowsDock;

/// <summary>
/// The dock's panels used to be per-pixel transparent WPF windows, only so they could have rounded corners. Windows
/// composes that kind of window in software, which cost tens of milliseconds of CPU for every repaint. These are
/// ordinary hardware-accelerated windows instead, and Windows 11 rounds the corners.
/// </summary>
internal static class DockWindowFrame
{
    private const int ExtendedStyle = -20;
    private const long Layered = 0x00080000;
    private const long ClickThrough = 0x00000020;
    private const int Cloak = 13;
    private const int CornerPreference = 33;
    private const int BorderColor = 34;

    /// <summary>
    /// Call before the window is shown. Rounds the corners, and keeps the window invisible until its first frame is
    /// drawn so a dark panel never opens with a flash of blank surface.
    /// </summary>
    internal static void Attach(Window window, Func<bool> staysHidden)
    {
        window.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle;
            var round = 2;
            DwmSetWindowAttribute(handle, CornerPreference, ref round, sizeof(int));
            // The panel draws its own hairline (which is also what pulses when a chat finishes), so no system border.
            var none = unchecked((int)0xFFFFFFFE);
            DwmSetWindowAttribute(handle, BorderColor, ref none, sizeof(int));
            SetHidden(window, true);
        };
        window.ContentRendered += (_, _) => SetHidden(window, staysHidden());
        // Belt and braces: if the first-frame event never arrives, the window still appears once the app is idle.
        window.Loaded += (_, _) => window.Dispatcher.BeginInvoke(
            () => SetHidden(window, staysHidden()), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    /// <summary>
    /// Makes a shown window fully see-through and click-through, or brings it back. The window stays "visible" to
    /// WPF, so anything it owns keeps working.
    /// </summary>
    internal static void SetHidden(Window window, bool hidden)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == 0)
            return;
        var style = GetWindowLongPtrW(handle, ExtendedStyle).ToInt64();
        if (hidden)
        {
            SetWindowLongPtrW(handle, ExtendedStyle, new nint(style | Layered | ClickThrough));
            SetLayeredWindowAttributes(handle, 0, 0, 2);
        }
        else if ((style & Layered) != 0)
            SetWindowLongPtrW(handle, ExtendedStyle, new nint(style & ~(Layered | ClickThrough)));
    }

    /// <summary>
    /// Hides a window from the screen entirely while it keeps rendering, so it can be prepared with nothing appearing.
    /// </summary>
    internal static void SetCloaked(Window window, bool cloaked)
    {
        var value = cloaked ? 1 : 0;
        DwmSetWindowAttribute(new WindowInteropHelper(window).EnsureHandle(), Cloak, ref value, sizeof(int));
    }

    [DllImport("dwmapi")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);

    [DllImport("user32", ExactSpelling = true)]
    private static extern nint GetWindowLongPtrW(nint window, int index);

    [DllImport("user32", ExactSpelling = true)]
    private static extern nint SetWindowLongPtrW(nint window, int index, nint value);

    [DllImport("user32")]
    private static extern bool SetLayeredWindowAttributes(nint window, uint key, byte alpha, uint flags);
}
