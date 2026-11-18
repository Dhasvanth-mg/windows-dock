using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WindowsDock;

public partial class DashboardWindow
{
    private enum DockSection { Widgets, Chats, Assistants }
    private enum DockSwipe { None, Left, Right, Down }
    private static readonly DashboardPage[] dashboardTabOrder =
        Enum.GetValues<DashboardPage>().Where(page => page != DashboardPage.Assistant).ToArray();
    private readonly TranslateTransform dashboardPageSlide = new();
    private DashboardPage lastDashboardPage = DashboardPage.Home;
    private long dashboardWheelAt;
    private int dashboardWheelTravel;
    private bool dashboardWheelIsHorizontal;
    private bool dashboardWheelFinished;
    private int nextDockSlide;
    private int wheelTravel;
    private long lastWheel;
    private long wheelLockedUntil;

    private bool DashboardReceivesGestures =>
        IsVisible && IsHitTestVisible && !compactAgents && !modalOpen && agentPanel == null;

    private nint HandleDashboardWheel(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        const int verticalWheel = 0x020A;
        const int horizontalWheel = 0x020E;
        if (message is not (verticalWheel or horizontalWheel) || !DashboardReceivesGestures ||
            Mouse.Captured != null || (wParam.ToInt64() & 0xFFFF) != 0 ||
            Keyboard.Modifiers != ModifierKeys.None || OpenComboBox(Keyboard.FocusedElement) != null)
            return 0;

        var coordinates = lParam.ToInt64();
        var point = PointFromScreen(new Point((short)(coordinates & 0xFFFF), (short)(coordinates >> 16)));
        if (InputHitTest(point) is not DependencyObject target || OpenComboBox(target) != null)
            return 0;

        var delta = (short)(wParam.ToInt64() >> 16);
        var horizontal = message == horizontalWheel;
        if (!horizontal && currentPage == DashboardPage.Widgets)
            return 0;
        var now = Environment.TickCount64;
        if (now - dashboardWheelAt > 220)
        {
            dashboardWheelTravel = 0;
            dashboardWheelFinished = false;
        }
        dashboardWheelAt = now;

        if (!horizontal && (delta <= 0 || CanScrollUp(target)))
        {
            // A scroll through content should finish there, even if its inertia reaches the top.
            dashboardWheelFinished = true;
            return 0;
        }

        handled = true;
        if (dashboardWheelFinished)
            return 0;
        if (dashboardWheelIsHorizontal != horizontal || Math.Sign(dashboardWheelTravel) != Math.Sign(delta))
            dashboardWheelTravel = 0;
        dashboardWheelIsHorizontal = horizontal;
        dashboardWheelTravel += delta;
        if (Math.Abs(dashboardWheelTravel) < 120)
            return 0;

        dashboardWheelFinished = true;
        Dispatcher.BeginInvoke(() =>
        {
            if (!DashboardReceivesGestures)
                return;
            if (!horizontal)
            {
                ShowWidgets();
                return;
            }

            var direction = Math.Sign(delta);
            var index = Array.IndexOf(dashboardTabOrder, currentPage) + direction;
            if (index < 0 || index >= dashboardTabOrder.Length)
                return;
            ShowPage(dashboardTabOrder[index]);
            dashboardWheelAt = Environment.TickCount64;
            PageHost.RenderTransform = dashboardPageSlide;
            SlideDockSurface(dashboardPageSlide, direction, 0);
        });
        return 0;
    }

    private static bool CanScrollUp(DependencyObject target)
    {
        for (var element = target; element != null;
            element = element is Visual visual ? VisualTreeHelper.GetParent(visual) : LogicalTreeHelper.GetParent(element))
            if (element is ScrollViewer { VerticalOffset: > 0, VerticalScrollBarVisibility: not ScrollBarVisibility.Disabled })
                return true;
        return false;
    }

    private FrameworkElement DockNavigation(Window window, DockSection selected)
    {
        var footer = new StackPanel { Margin = new Thickness(0, 10, 0, -6) };
        var strip = Columns(1, 1, 1);
        var labels = new[] { "Widgets", "Coding chats", "Assistants" };
        for (var i = 0; i < labels.Length; i++)
        {
            var section = (DockSection)i;
            var current = section == selected;
            var button = MakeButton(Text(labels[i], 12, current ? AssistantInk : AssistantSecondary, current),
                labels[i], () => { if (!current) OpenDockSection(selected, section); });
            button.Style = (Style)FindResource("AssistantPickerButton");
            button.Foreground = AssistantInk;
            button.Tag = current ? "Selected" : null;
            button.MinHeight = 29;
            button.Padding = new Thickness(2, 4, 2, 4);
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
            ToolTipService.SetIsEnabled(button, false);
            if (current)
                AutomationProperties.SetItemStatus(button, "Current section");
            Put(strip, button, i);
        }
        footer.Children.Add(strip);
        footer.Children.Add(DockGrip(window, swipe =>
        {
            if (swipe == DockSwipe.Down)
            {
                CloseSurface();
                return;
            }
            var target = (int)selected + (swipe == DockSwipe.Left ? 1 : -1);
            if (target >= 0 && target < labels.Length)
                OpenDockSection(selected, (DockSection)target);
        }, null, true));
        return footer;

        void CloseSurface()
        {
            if (window == this)
                Dismiss();
            else
                window.Close();
        }
    }

    private void OpenDockSection(DockSection from, DockSection to)
    {
        nextDockSlide = to > from ? 1 : -1;
        try
        {
            switch (to)
            {
                case DockSection.Widgets: ShowWidgets(); break;
                case DockSection.Chats: OpenAgentGrid(); break;
                case DockSection.Assistants: ShowAssistantPicker(); break;
            }
        }
        finally { nextDockSlide = 0; }
    }

    private FrameworkElement DockGrip(Window window, Action<DockSwipe> commit, string? hint, bool horizontal)
    {
        var markOffset = new TranslateTransform();
        var mark = new Border
        {
            Width = 36, Height = 3, CornerRadius = new CornerRadius(2), Background = AssistantFaint,
            Opacity = .7, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false, RenderTransform = markOffset
        };
        var grip = new Border
        {
            Height = 22, Background = Brushes.Transparent,
            Cursor = Cursors.SizeAll
        };
        if (hint != null)
        {
            var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            body.Children.Add(mark);
            var label = Text(hint, 11, AssistantSecondary);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.Margin = new Thickness(0, 3, 0, 0);
            body.Children.Add(label);
            grip.Height = 34;
            grip.Child = body;
        }
        else
            grip.Child = mark;

        Point origin = default;
        bool mouse = false;
        TouchDevice? touch = null;
        grip.MouseLeftButtonDown += (_, e) =>
        {
            if (touch != null || !grip.CaptureMouse())
                return;
            mouse = true;
            origin = e.GetPosition(window);
            mark.Opacity = 1;
            e.Handled = true;
        };
        grip.MouseMove += (_, e) =>
        {
            if (mouse)
                Move(e.GetPosition(window));
        };
        grip.MouseLeftButtonUp += (_, e) =>
        {
            if (!mouse)
                return;
            mouse = false;
            grip.ReleaseMouseCapture();
            e.Handled = true;
            Finish(e.GetPosition(window));
        };
        grip.LostMouseCapture += (_, _) => { mouse = false; Reset(); };
        grip.TouchDown += (_, e) =>
        {
            if (mouse || touch != null || !grip.CaptureTouch(e.TouchDevice))
                return;
            touch = e.TouchDevice;
            origin = e.GetTouchPoint(window).Position;
            mark.Opacity = 1;
            e.Handled = true;
        };
        grip.TouchMove += (_, e) =>
        {
            if (e.TouchDevice == touch)
            {
                Move(e.GetTouchPoint(window).Position);
                e.Handled = true;
            }
        };
        grip.TouchUp += (_, e) =>
        {
            if (e.TouchDevice != touch)
                return;
            touch = null;
            grip.ReleaseTouchCapture(e.TouchDevice);
            e.Handled = true;
            Finish(e.GetTouchPoint(window).Position);
        };
        grip.LostTouchCapture += (_, e) =>
        {
            if (touch == null || e.TouchDevice == touch)
            {
                touch = null;
                Reset();
            }
        };

        if (horizontal)
        {
            void Attach()
            {
                if (PresentationSource.FromVisual(window) is HwndSource source)
                    source.AddHook(HandleWheel);
            }

            if (PresentationSource.FromVisual(window) == null)
                window.SourceInitialized += (_, _) => Attach();
            else
                Attach();
        }
        return grip;

        void Reset()
        {
            mark.Opacity = .7;
            markOffset.X = markOffset.Y = 0;
        }

        void Move(Point point)
        {
            if (!SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast)
                return;
            var delta = point - origin;
            markOffset.X = Math.Clamp(delta.X * .35, -28, 28);
            markOffset.Y = Math.Clamp(delta.Y * .3, 0, 9);
        }

        void Finish(Point point)
        {
            Reset();
            var direction = ClassifyDockSwipe(point - origin);
            if (direction != DockSwipe.None)
                commit(direction);
        }

        nint HandleWheel(nint _, int message, nint wParam, nint lParam, ref bool handled)
        {
            const int horizontalWheel = 0x020E;
            if (message != horizontalWheel || !grip.IsVisible)
                return 0;
            var coordinates = lParam.ToInt64();
            var point = grip.PointFromScreen(new Point((short)(coordinates & 0xFFFF), (short)(coordinates >> 16)));
            if (point.X < 0 || point.Y < 0 || point.X >= grip.ActualWidth || point.Y >= grip.ActualHeight)
                return 0;
            handled = true;
            var now = Environment.TickCount64;
            if (now < wheelLockedUntil)
                return 0;
            if (now - lastWheel > 200)
                wheelTravel = 0;
            lastWheel = now;
            var delta = (short)(wParam.ToInt64() >> 16);
            if (Math.Sign(delta) != Math.Sign(wheelTravel))
                wheelTravel = 0;
            wheelTravel += delta;
            if (Math.Abs(wheelTravel) >= 120)
            {
                wheelTravel = 0;
                wheelLockedUntil = now + 350;
                var direction = delta > 0 ? DockSwipe.Left : DockSwipe.Right;
                window.Dispatcher.BeginInvoke(() => { if (grip.IsVisible) commit(direction); });
            }
            return 0;
        }
    }

    private static DockSwipe ClassifyDockSwipe(Vector distance)
    {
        var x = Math.Abs(distance.X);
        var y = Math.Abs(distance.Y);
        if (x >= 48 && x >= y * 1.35)
            return distance.X < 0 ? DockSwipe.Left : DockSwipe.Right;
        return distance.Y >= 48 && y >= x * 1.35 ? DockSwipe.Down : DockSwipe.None;
    }

    private static void SlideDockSurface(TranslateTransform transform, int direction, double vertical)
    {
        if (!SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast)
            return;
        var property = direction == 0 ? TranslateTransform.YProperty : TranslateTransform.XProperty;
        transform.BeginAnimation(TranslateTransform.XProperty, null);
        transform.BeginAnimation(TranslateTransform.YProperty, null);
        transform.BeginAnimation(property, new DoubleAnimation(direction == 0 ? vertical : 26 * direction, 0,
            TimeSpan.FromMilliseconds(165)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    // Pulling the header down puts the dashboard away, the same as pulling down on the taskbar button. It works from
    // anywhere on the header, tabs included; a press that moves less than a short pull is still an ordinary click.
    private void AttachHeaderSwipe()
    {
        Point? origin = null;
        DashboardHeader.PreviewMouseLeftButtonDown += (_, e) => origin = e.GetPosition(this);
        DashboardHeader.PreviewMouseLeftButtonUp += (_, _) => origin = null;
        DashboardHeader.MouseLeave += (_, _) =>
        {
            if (Mouse.LeftButton != MouseButtonState.Pressed)
                origin = null;
        };
        PreviewMouseMove += (_, e) =>
        {
            if (origin is not { } start || e.LeftButton != MouseButtonState.Pressed)
            {
                origin = null;
                return;
            }
            var position = e.GetPosition(this);
            var down = position.Y - start.Y;
            if (down < 36 || down < Math.Abs(position.X - start.X) * 1.35)
                return;
            origin = null;
            Mouse.Capture(null);
            if (DashboardReceivesGestures || hostedWindow != null)
                Dismiss();
        };
    }
}
