using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ellipse = System.Windows.Shapes.Ellipse;
using Polyline = System.Windows.Shapes.Polyline;

namespace WindowsDock;

public partial class DashboardWindow
{
    private ulong lastIdle, lastTotal;
    private long lastNetwork;
    private long lastSample;
    private readonly Queue<double> cpuHistory = new();
    private readonly Queue<double> networkHistory = new();

    private UIElement StatsPage()
    {
        var grid = Columns(1, 1, 1, 1);
        var cpu = StatsCard(grid, 0, "Processor", green, true);
        var memory = StatsCard(grid, 1, "Memory", purple, false);
        var disk = StatsCard(grid, 2, "System drive", Brush("#52DAC9"), false);
        var network = StatsCard(grid, 3, "Network", accent, true);
        updatePage = () =>
        {
            var sample = ReadMetrics();
            cpu($"{sample.Cpu:0}%", "CPU utilization", sample.Cpu, cpuHistory);
            memory(FormatBytes(sample.UsedMemory), $"{FormatBytes(sample.TotalMemory)} installed", sample.Memory, null);
            disk(FormatBytes(sample.FreeDisk), "Available", sample.DiskUsed, null);
            var peak = Math.Max(1024, networkHistory.Count == 0 ? 0 : networkHistory.Max());
            network($"{FormatBytes(sample.Network)}/s", "Total traffic", sample.Network / peak * 100,
                networkHistory.Select(value => value / peak * 100));
        };
        return grid;
    }

    private Action<string, string, double, IEnumerable<double>?> StatsCard(Grid grid, int index, string name,
        Brush color, bool graph)
    {
        var stack = new StackPanel();
        var heading = new StackPanel { Orientation = Orientation.Horizontal };
        heading.Children.Add(new Ellipse { Width = 6, Height = 6, Fill = color, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) });
        heading.Children.Add(Text(name, 11, subdued, true));
        stack.Children.Add(heading);
        var value = Text("—", 24, strong: true);
        value.Margin = new Thickness(0, 4, 0, 0);
        stack.Children.Add(value);
        var detail = Text("", 10, muted);
        stack.Children.Add(detail);

        var line = new Polyline { Stroke = color, StrokeThickness = 1.6 };
        var graphArea = new Grid { Height = 21, Margin = new Thickness(0, 5, 0, 0), ClipToBounds = true };
        if (graph)
            graphArea.Children.Add(new Viewbox { Stretch = Stretch.Fill, Child = new Canvas { Width = 180, Height = 24, Children = { line } } });
        else
        {
            var bar = Progress(color);
            graphArea.Children.Add(bar);
        }
        stack.Children.Add(graphArea);
        Put(grid, Card(stack), index);

        return (number, caption, percentage, points) =>
        {
            value.Text = number;
            detail.Text = caption;
            if (!graph)
                ((ProgressBar)graphArea.Children[0]).Value = percentage;
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
        Height = 4, Minimum = 0, Maximum = 100, Foreground = color,
        Background = Brush("#22FFFFFF"), BorderThickness = new Thickness(0)
    };

    private Metrics ReadMetrics()
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
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces().Where(item =>
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
        AddSample(cpuHistory, cpu);
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
