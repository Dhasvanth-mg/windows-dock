using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace WindowsDock;

public partial class MainWindow
{
    private const int RevealHotkey = 1;
    private readonly Dictionary<string, TextBlock> widgetReadouts = new();
    private static readonly string[] AvailableWidgets = ["Battery", "Network", "Memory", "Date", "Media", "Assistant"];
    private long lastInteraction;
    private bool hotkeyRegistered;

    private void InitializeDesktopControls()
    {
        var tray = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "Windows Dock",
            Visible = true,
            ContextMenuStrip = new Forms.ContextMenuStrip()
        };
        tray.Click += (_, e) =>
        {
            if (e is Forms.MouseEventArgs { Button: Forms.MouseButtons.Left })
                Dispatcher.BeginInvoke(() => { Reveal(); ShowStudio(); });
        };
        tray.ContextMenuStrip.Items.Add("Reveal dock", null, (_, _) => Dispatcher.BeginInvoke(Reveal));
        tray.ContextMenuStrip.Items.Add("Customization Studio", null, (_, _) => Dispatcher.BeginInvoke(ShowStudio));
        tray.ContextMenuStrip.Items.Add("Exit Windows Dock", null, (_, _) => Dispatcher.BeginInvoke(Close));

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        timer.Tick += (_, _) => TrackReveal();
        HwndSource? source = null;
        SourceInitialized += (_, _) =>
        {
            source = (HwndSource)PresentationSource.FromVisual(this);
            source.AddHook(DesktopMessage);
            hotkeyRegistered = RegisterHotKey(source.Handle, RevealHotkey, 0x4003, 0x44);
            lastInteraction = Environment.TickCount64;
            timer.Start();
        };
        Closed += (_, _) =>
        {
            timer.Stop();
            if (hotkeyRegistered && source is { IsDisposed: false })
                UnregisterHotKey(source.Handle, RevealHotkey);
            tray.Visible = false;
            tray.ContextMenuStrip.Dispose();
            tray.Dispose();
        };
    }

    private nint DesktopMessage(nint window, int message, nint key, nint data, ref bool handled)
    {
        if (message == 0x312 && key == RevealHotkey)
        {
            Reveal();
            handled = true;
        }
        return 0;
    }

    private void Reveal()
    {
        lastInteraction = Environment.TickCount64;
        if (!IsVisible)
        {
            Show();
            PositionDock();
        }
    }

    private void TrackReveal()
    {
        if (!settings.AutoHide)
        {
            if (!IsVisible)
                Reveal();
            return;
        }

        var handle = new WindowInteropHelper(this).Handle;
        var screen = Forms.Screen.FromHandle(handle);
        var cursor = Forms.Cursor.Position;
        var onScreen = screen.Bounds.Contains(cursor);
        var atEdge = onScreen && (cursor.Y >= screen.Bounds.Bottom - settings.EdgeDepth ||
            Math.Abs(cursor.Y - screen.WorkingArea.Bottom) <= settings.EdgeDepth);
        if (atEdge)
        {
            Reveal();
            return;
        }

        if (!IsVisible)
            return;

        if (IsMouseOver || IsKeyboardFocusWithin || studio?.IsActive == true || assistant?.IsVisible == true ||
            HasOpenMenu())
            lastInteraction = Environment.TickCount64;
        else if (Environment.TickCount64 - lastInteraction >= settings.HideDelay)
            Hide();
    }

    private bool HasOpenMenu() => quickControls?.IsOpen == true || Items.Children.OfType<Button>().Concat(DockActions.Children.OfType<Button>())
        .Any(button => button.ContextMenu?.IsOpen == true);

    private void ConfigureCenteredMenu(ContextMenu menu)
    {
        menu.PlacementTarget = Surface;
        menu.Placement = PlacementMode.Custom;
        menu.MaxHeight = Math.Max(100, SystemParameters.WorkArea.Height * 0.7);
        menu.CustomPopupPlacementCallback = (popup, target, _) =>
        [
            new CustomPopupPlacement(new Point((target.Width - popup.Width) / 2, -popup.Height - 10),
                PopupPrimaryAxis.Horizontal),
            new CustomPopupPlacement(new Point((target.Width - popup.Width) / 2, target.Height + 10),
                PopupPrimaryAxis.Horizontal)
        ];
        menu.Background = new SolidColorBrush(Color.FromRgb(27, 31, 39));
        menu.Foreground = Brushes.White;
        menu.Padding = new Thickness(8);
        menu.Opened += (_, _) => lastInteraction = Environment.TickCount64;
    }

    private void RenderWidgets()
    {
        foreach (var name in settings.Widgets.Distinct().Intersect(AvailableWidgets))
        {
            if (name == "Media")
            {
                AddMediaWidget();
                continue;
            }
            if (name == "Assistant")
            {
                AddAssistantWidget();
                continue;
            }
            var readout = new TextBlock { TextAlignment = TextAlignment.Center };
            widgetReadouts[name] = readout;
            AddButton(name + " widget", readout, () =>
            {
                var target = name switch
                {
                    "Battery" => "ms-settings:powersleep",
                    "Network" => "ms-settings:network",
                    "Memory" => "taskmgr.exe",
                    _ => "ms-settings:dateandtime"
                };
                Launch(target);
            });
        }
    }

    private void UpdateWidgets()
    {
        foreach (var (name, readout) in widgetReadouts)
        {
            readout.Text = name switch
            {
                "Battery" => Forms.SystemInformation.PowerStatus.BatteryChargeStatus.HasFlag(Forms.BatteryChargeStatus.NoSystemBattery)
                    ? "No battery" : $"Battery\n{Forms.SystemInformation.PowerStatus.BatteryLifePercent:P0}",
                "Network" => System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable() ? "Network\nConnected" : "Network\nOffline",
                "Memory" => MemoryReadout(),
                _ => DateTime.Now.ToString("ddd\nMMM d")
            };
        }
    }

    private static string MemoryReadout()
    {
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        return GlobalMemoryStatusEx(ref memory) ? $"Memory\n{memory.Load}%" : "Memory\nUnavailable";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile,
            TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }

    [DllImport("kernel32")]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus memory);

    [DllImport("user32")]
    private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);

    [DllImport("user32")]
    private static extern bool UnregisterHotKey(nint window, int id);
}
