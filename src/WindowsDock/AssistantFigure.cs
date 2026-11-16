using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace WindowsDock;

/// <summary>
/// One of the dock's own characters, drawn from its vector portrait and given a little life: it blinks now and
/// then, hops when you reach for it, sways while idle and wobbles while it is thinking. All of it is skipped when
/// Windows animations are off, and nothing runs while the figure is not on screen.
/// </summary>
internal sealed class AssistantFigure : Border
{
    // Where each character's eyes are in its 32 by 32 drawing, and the colour that closes over them.
    private static readonly Dictionary<string, (string Lid, Rect[] Eyes)> Faces = new()
    {
        ["research"] = ("#97CBFB", [new Rect(23.2, 8.7, 3.6, 3.6)]),
        ["housekeeping"] = ("#A7DE98", [new Rect(9.2, 9.8, 3.6, 3.7), new Rect(19.2, 9.8, 3.6, 3.7)]),
        ["coding"] = ("#332832", [new Rect(10.8, 15.8, 3.4, 4.4), new Rect(17.8, 16.8, 3.4, 2.4)]),
        ["casual"] = ("#D0A5F1", [new Rect(10.6, 11.8, 2.8, 2.8), new Rect(16.6, 11.8, 2.8, 2.8)])
    };
    private static readonly Random Chance = new();

    private readonly TranslateTransform lift = new();
    private readonly ScaleTransform squash = new(1, 1);
    private readonly RotateTransform tilt = new();
    private readonly List<ScaleTransform> lids = [];
    private readonly DispatcherTimer blinkTimer = new(DispatcherPriority.Background);
    private readonly double size;
    private readonly bool lively;
    private bool thinking;

    internal AssistantFigure(AssistantPersona persona, double size, bool lively)
    {
        this.size = size;
        this.lively = lively;
        Width = Height = size;
        HorizontalAlignment = HorizontalAlignment.Center;
        var art = size * .82;
        var canvas = new Canvas { Width = 32, Height = 32 };
        canvas.Children.Add(new Image { Source = persona.Face, Width = 32, Height = 32 });
        if (Faces.TryGetValue(persona.Id, out var face))
        {
            var lid = new SolidColorBrush((Color)ColorConverter.ConvertFromString(face.Lid));
            lid.Freeze();
            foreach (var eye in face.Eyes)
            {
                var scale = new ScaleTransform(1, 0);
                var cover = new Rectangle
                {
                    Width = eye.Width, Height = eye.Height, Fill = lid, RadiusX = eye.Width / 2.6, RadiusY = eye.Width / 2.6,
                    RenderTransform = scale, RenderTransformOrigin = new Point(.5, 0), IsHitTestVisible = false
                };
                Canvas.SetLeft(cover, eye.X);
                Canvas.SetTop(cover, eye.Y);
                canvas.Children.Add(cover);
                lids.Add(scale);
            }
        }
        Child = new Viewbox
        {
            Width = art, Height = art, Child = canvas, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, RenderTransformOrigin = new Point(.5, .9),
            RenderTransform = new TransformGroup { Children = { squash, tilt, lift } }
        };

        blinkTimer.Tick += (_, _) =>
        {
            Blink();
            blinkTimer.Interval = TimeSpan.FromMilliseconds(2400 + Chance.Next(4200));
        };
        IsVisibleChanged += (_, _) => Wake(IsVisible);
        Unloaded += (_, _) => Wake(false);
        MouseEnter += (_, _) => Hop();
    }

    private static bool Motion => SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;

    /// <summary>True while the character is working on an answer.</summary>
    internal bool Thinking
    {
        get => thinking;
        set
        {
            if (thinking == value)
                return;
            thinking = value;
            Wake(IsVisible);
        }
    }

    private void Wake(bool awake)
    {
        lift.BeginAnimation(TranslateTransform.YProperty, null);
        tilt.BeginAnimation(RotateTransform.AngleProperty, null);
        blinkTimer.Stop();
        // The small figures in the dashboard header stay still until you reach for them, so an open dashboard that
        // nobody is touching draws nothing.
        if (!awake || !Motion || !lively && !thinking)
            return;
        blinkTimer.Interval = TimeSpan.FromMilliseconds(900 + Chance.Next(3600));
        blinkTimer.Start();
        if (thinking)
        {
            // A quick side-to-side wobble: visibly busy without being a spinner.
            tilt.BeginAnimation(RotateTransform.AngleProperty, Loop(-5, 5, 420));
            lift.BeginAnimation(TranslateTransform.YProperty, Loop(0, -size * .05, 420));
        }
        else if (lively)
            lift.BeginAnimation(TranslateTransform.YProperty, Loop(0, -size * .035, 1500 + Chance.Next(500)));
    }

    internal void Blink()
    {
        if (!Motion || !IsVisible)
            return;
        var close = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(170) };
        close.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(.4)));
        close.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(.6)));
        close.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
        foreach (var lid in lids)
            lid.BeginAnimation(ScaleTransform.ScaleYProperty, close);
    }

    /// <summary>A small jump with a squash on landing. Used on hover and when a reply arrives.</summary>
    internal void Hop()
    {
        if (!Motion || !IsVisible || thinking)
            return;
        var jump = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(330) };
        jump.KeyFrames.Add(new EasingDoubleKeyFrame(-size * .16, KeyTime.FromPercent(.42), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        jump.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
        jump.Completed += (_, _) => Wake(IsVisible);
        lift.BeginAnimation(TranslateTransform.YProperty, jump);
        var land = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(460) };
        land.KeyFrames.Add(new LinearDoubleKeyFrame(1.06, KeyTime.FromPercent(.3)));
        land.KeyFrames.Add(new LinearDoubleKeyFrame(.9, KeyTime.FromPercent(.76)));
        land.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(1)));
        squash.BeginAnimation(ScaleTransform.ScaleYProperty, land);
    }

    private static DoubleAnimation Loop(double from, double to, int milliseconds)
    {
        var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds))
        {
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        // Half the usual frame rate is plenty for a slow sway and halves the work.
        Timeline.SetDesiredFrameRate(animation, 30);
        return animation;
    }
}
