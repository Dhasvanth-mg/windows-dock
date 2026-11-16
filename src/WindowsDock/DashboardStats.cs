using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Polyline = System.Windows.Shapes.Polyline;

namespace WindowsDock;

public partial class DashboardWindow
{
    private ulong lastIdle, lastTotal;
    private long lastNetwork;
    private long lastSample;
    private NetworkInterface[]? adapters;
    private long adaptersListed;
    private readonly Queue<double> cpuHistory = new();
    private readonly Queue<double> networkHistory = new();

    // Desk: the machine, the things you keep within reach, and what is on the wall behind them.
    private UIElement DeskPage()
    {
        var system = SystemPane(out var update);
        updatePage = update;
        return FillPage(Panes((1, system), (1, ShelfPane()), (1, WallpaperPane())));
    }

    // Live readings as plain rows. No gauges; the numbers are the display.
    private FrameworkElement SystemPane(out Action update)
    {
        var pane = new StackPanel();
        pane.Children.Add(SectionTitle("This PC"));
        var cpu = MetricRow(pane, "CPU", true);
        var memory = MetricRow(pane, "Memory", false);
        var disk = MetricRow(pane, "Drive " + Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\'), false);
        var network = MetricRow(pane, "Network", true);

        var hasBattery = !System.Windows.Forms.SystemInformation.PowerStatus.BatteryChargeStatus
            .HasFlag(System.Windows.Forms.BatteryChargeStatus.NoSystemBattery);
        var power = MetricRow(pane, hasBattery ? "Battery" : "Power", false, last: true);
        var footer = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var lockButton = ActionButton("Lock", () =>
        {
            if (LockWorkStation())
                Dismiss();
        });
        System.Windows.Automation.AutomationProperties.SetName(lockButton, "Lock Windows");
        DockPanel.SetDock(lockButton, Dock.Right);
        footer.Children.Add(lockButton);
        var uptime = Text("", 12, AssistantSecondary);
        uptime.VerticalAlignment = VerticalAlignment.Center;
        footer.Children.Add(uptime);
        pane.Children.Add(footer);
        // The spare room under the readings holds the three things you reach for when a machine misbehaves.
        var tools = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var settings = QuietButton("Power settings", () => OpenTarget("ms-settings:powersleep"));
        if (hasBattery && controllerPid > 0)
        {
            settings.ToolTip = "Windows Dock warns at 25%, 20%, 15%, 10% and 5% while unplugged.";
            ToolTipService.SetIsEnabled(settings, true);
        }
        foreach (var link in new[]
        {
            QuietButton("Task Manager", () => OpenTarget("taskmgr.exe")),
            QuietButton("Snip the screen", () =>
            {
                Dismiss();
                OpenTarget("ms-screenclip:");
            }),
            settings
        })
        {
            link.Margin = new Thickness(0, 0, 16, 2);
            tools.Children.Add(link);
        }
        pane.Children.Add(tools);

        update = () =>
        {
            var sample = ReadMetrics();
            cpu($"{sample.Cpu:0}%", "", sample.Cpu, cpuHistory);
            memory(FormatBytes(sample.UsedMemory), $"of {FormatBytes(sample.TotalMemory)}", sample.Memory, null);
            disk(FormatBytes(sample.FreeDisk), "free", sample.DiskUsed, null);
            var peak = Math.Max(1024, networkHistory.Count == 0 ? 0 : networkHistory.Max());
            network($"{FormatBytes(sample.Network)}/s", "", sample.Network / peak * 100,
                networkHistory.Select(value => value / peak * 100));

            var battery = System.Windows.Forms.SystemInformation.PowerStatus;
            var percent = Math.Clamp(battery.BatteryLifePercent, 0, 1);
            var plugged = battery.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
            if (hasBattery)
                power($"{percent:P0}", plugged ? "charging" : "", percent * 100, null, !plugged && percent < .2 ? amber : null);
            else
                power("Plugged in", "", 100, null);
            var running = TimeSpan.FromMilliseconds(Environment.TickCount64);
            uptime.Text = "Up for " + (running.Days > 0 ? $"{running.Days}d {running.Hours}h" : $"{running.Hours}h {running.Minutes}m");
        };
        return pane;
    }

    private delegate void MetricUpdate(string number, string caption, double percentage, IEnumerable<double>? points, Brush? tone = null);

    private MetricUpdate MetricRow(StackPanel host, string name, bool graph, bool last = false)
    {
        var row = new Grid { MinHeight = 44 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(66) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        var label = Text(name, 12, subdued);
        label.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(label);
        var value = Text("", 15, strong: true);
        Typography.SetNumeralAlignment(value, FontNumeralAlignment.Tabular);
        var detail = Text("", 11, muted);
        detail.Margin = new Thickness(6, 0, 0, 1);
        detail.VerticalAlignment = VerticalAlignment.Bottom;
        var reading = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        reading.Children.Add(value);
        reading.Children.Add(detail);
        Grid.SetColumn(reading, 1);
        row.Children.Add(reading);

        var line = new Polyline { Stroke = AssistantInk, StrokeThickness = 1.4, StrokeLineJoin = PenLineJoin.Round };
        var bar = Progress(AssistantInk);
        FrameworkElement visual = graph
            ? new Viewbox { Stretch = Stretch.Fill, Height = 22, Child = new Canvas { Width = 180, Height = 24, Children = { line } } }
            : bar;
        visual.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(visual, 2);
        row.Children.Add(visual);
        host.Children.Add(new Border
        {
            Child = row, BorderBrush = AssistantRule, BorderThickness = new Thickness(0, 0, 0, last ? 0 : 1)
        });
        System.Windows.Automation.AutomationProperties.SetName(row, name);

        return (number, caption, percentage, points, tone) =>
        {
            value.Text = number;
            detail.Text = caption;
            if (!graph)
            {
                bar.Value = percentage;
                bar.Foreground = tone ?? AssistantInk;
            }
            else if (points is not null)
            {
                var data = points.ToArray();
                line.Points = new PointCollection(data.Select((sample, i) =>
                    new Point(180.0 * i / Math.Max(1, data.Length - 1), 23 - Math.Clamp(sample, 0, 100) * 0.21)));
            }
        };
    }

    private Action<double, string> MetricBar(StackPanel stack, string label, Brush color)
    {
        var line = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
        var reading = Text("", 10, subdued);
        DockPanel.SetDock(reading, Dock.Right);
        line.Children.Add(reading);
        line.Children.Add(Text(label, 10, subdued, true));
        stack.Children.Add(line);
        var bar = Progress(color);
        bar.Margin = new Thickness(0, 0, 0, 3);
        stack.Children.Add(bar);
        return (value, display) => { reading.Text = display; bar.Value = value; };
    }

    private ProgressBar Progress(Brush color) => new()
    {
        Height = 3, Minimum = 0, Maximum = 100, Foreground = color,
        Background = trackFill, BorderThickness = new Thickness(0)
    };

    // "full" also reads the drive and every network adapter, which only the Desk page needs. Today asks for the
    // light reading so its once-a-second tick stays cheap.
    private Metrics ReadMetrics(bool full = true)
    {
        var cpu = 0d;
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            var total = kernel + user;
            if (lastTotal > 0 && total > lastTotal && idle >= lastIdle)
                cpu = Math.Clamp((1.0 - (double)(idle - lastIdle) / (total - lastTotal)) * 100, 0, 100);
            lastIdle = idle;
            lastTotal = total;
        }

        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        GlobalMemoryStatusEx(ref memory);
        AddSample(cpuHistory, cpu);
        if (!full)
            return new Metrics(cpu, memory.Load, memory.TotalPhysical - memory.AvailablePhysical, memory.TotalPhysical, 0, 0, 0);
        var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
        var freeDisk = 0L;
        var diskUsed = 0d;
        try
        {
            freeDisk = drive.AvailableFreeSpace;
            if (drive.TotalSize > 0)
                diskUsed = (1 - (double)freeDisk / drive.TotalSize) * 100;
        }
        catch (IOException) { }

        var received = 0L;
        // Listing adapters is the slow part (about 15 ms), and the list rarely changes, so keep it for half a minute.
        if (adapters == null || Environment.TickCount64 - adaptersListed > 30_000)
        {
            adapters = NetworkInterface.GetAllNetworkInterfaces();
            adaptersListed = Environment.TickCount64;
        }
        foreach (var adapter in adapters.Where(item =>
            item.OperationalStatus == OperationalStatus.Up && item.NetworkInterfaceType != NetworkInterfaceType.Loopback))
        {
            try
            {
                var traffic = adapter.GetIPStatistics();
                received += traffic.BytesReceived + traffic.BytesSent;
            }
            catch (NetworkInformationException) { }
        }
        var now = Environment.TickCount64;
        var speed = lastSample == 0 || now == lastSample ? 0 : Math.Max(0, (received - lastNetwork) * 1000.0 / (now - lastSample));
        lastNetwork = received;
        lastSample = now;
        AddSample(networkHistory, speed);
        return new Metrics(cpu, memory.Load, memory.TotalPhysical - memory.AvailablePhysical,
            memory.TotalPhysical, freeDisk, diskUsed, speed);
    }

    private static void AddSample(Queue<double> history, double value)
    {
        history.Enqueue(value);
        if (history.Count > 36)
            history.Dequeue();
    }

    private static string FormatBytes(double bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var index = 0;
        while (bytes >= 1024 && index < units.Length - 1)
        {
            bytes /= 1024;
            index++;
        }
        return $"{bytes:0.#} {units[index]}";
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

    [DllImport("kernel32")]
    private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);

    private sealed record Metrics(double Cpu, double Memory, ulong UsedMemory, ulong TotalMemory, long FreeDisk,
        double DiskUsed, double Network);
}
