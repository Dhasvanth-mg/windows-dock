using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WindowsDock;

public partial class DashboardWindow
{
    private DateTime completionFileStamp;
    private DateTimeOffset lastCompletion;
    private DateTimeOffset paintedCompletion;
    private long completionAnimationStarted;

    private void UpdateCompletionCue()
    {
        if (!SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast)
            return;
        DateTime stamp;
        try { stamp = File.GetLastWriteTimeUtc(DockData.FilePath(DockCompletion.Marker)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return; }
        if (stamp != completionFileStamp)
        {
            completionFileStamp = stamp;
            lastCompletion = DockData.Read<DateTimeOffset>(DockCompletion.Marker);
        }
        if (lastCompletion == paintedCompletion || DateTimeOffset.UtcNow - lastCompletion > TimeSpan.FromSeconds(12))
            return;
        if (agentPanel is { IsVisible: true, Content: Border card })
            Glow(card);
        else if (IsVisible && Opacity > 0)
            Glow(Shell);
        else
            return;
        paintedCompletion = lastCompletion;
    }

    private void Glow(Border border)
    {
        var now = Environment.TickCount64;
        if (now - completionAnimationStarted < 900)
            return;
        completionAnimationStarted = now;
        var original = border.BorderBrush;
        var rim = new SolidColorBrush((original as SolidColorBrush)?.Color ?? Colors.DimGray);
        border.SetCurrentValue(Border.BorderBrushProperty, rim);
        var pulse = new ColorAnimation(Color.FromRgb(165, 247, 199), TimeSpan.FromMilliseconds(410))
        {
            AutoReverse = true,
            FillBehavior = FillBehavior.Stop,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        pulse.Completed += (_, _) =>
        {
            if (ReferenceEquals(border.BorderBrush, rim))
                border.SetCurrentValue(Border.BorderBrushProperty, original);
        };
        rim.BeginAnimation(SolidColorBrush.ColorProperty, pulse);
    }
}
