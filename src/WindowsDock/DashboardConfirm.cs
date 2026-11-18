using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace WindowsDock;

// "Are you sure?" questions used to open a Windows message box over the dock. Inside the dashboard they now rise as a
// strip along the bottom with the question and two buttons, and the dashboard stays usable around it. A window of
// its own that is not inside the dashboard (the canvas editors, the poker tables) still gets a plain message box.
public partial class DashboardWindow
{
    private TaskCompletionSource<bool>? pendingConfirm;

    /// <summary>Withdraws an open question as a no, when what it was about has gone away or Esc was pressed.</summary>
    private bool CancelPendingConfirm()
    {
        if (pendingConfirm is not { } open)
            return false;
        pendingConfirm = null;
        ConfirmBar.Visibility = Visibility.Collapsed;
        ConfirmBar.Child = null;
        open.TrySetResult(false);
        return true;
    }

    /// <summary>Asks a yes-or-no question and completes with the answer. Esc, Cancel or asking again means no.</summary>
    private Task<bool> ConfirmAsync(Window owner, string question, string yes, bool destructive = true)
    {
        if (owner != this && !IsHosted(owner))
            return Task.FromResult(MessageBox.Show(owner, question, "Windows Dock", MessageBoxButton.YesNo,
                MessageBoxImage.None, MessageBoxResult.No) == MessageBoxResult.Yes);

        pendingConfirm?.TrySetResult(false);
        var answer = new TaskCompletionSource<bool>();
        pendingConfirm = answer;
        var text = Text(question, 13, AssistantInk);
        text.TextWrapping = TextWrapping.Wrap;
        text.VerticalAlignment = VerticalAlignment.Center;
        var accept = ActionButton(yes, () => Finish(true));
        if (destructive)
        {
            accept.Foreground = bad;
            accept.BorderBrush = bad;
        }
        else
            MarkPrimary(accept, true);
        accept.Margin = new Thickness(12, 0, 6, 0);
        var cancel = QuietButton("Cancel", () => Finish(false));
        cancel.Margin = new Thickness(6, 0, 4, 0);
        var row = new DockPanel();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(accept);
        buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Right);
        row.Children.Add(buttons);
        row.Children.Add(text);
        ConfirmBar.Child = row;
        ConfirmBar.Background = SystemParameters.HighContrast ? SystemColors.WindowBrush : controlSelected;
        ConfirmBar.BorderBrush = AssistantRule;
        ConfirmBar.BorderThickness = new Thickness(1);
        ConfirmBar.CornerRadius = new CornerRadius(10);
        ConfirmBar.Padding = new Thickness(14, 10, 10, 10);
        ConfirmBar.Visibility = Visibility.Visible;
        AutomationProperties.SetName(ConfirmBar, question);
        AutomationProperties.SetLiveSetting(ConfirmBar, AutomationLiveSetting.Assertive);
        ConfirmBar.PreviewKeyDown += Escape;
        if (!SystemParameters.HighContrast)
        {
            var rise = new System.Windows.Media.TranslateTransform(0, 14);
            ConfirmBar.RenderTransform = rise;
            rise.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
                new DoubleAnimation(14, 0, new Duration(TimeSpan.FromMilliseconds(160))) { EasingFunction = new QuadraticEase() });
        }
        Dispatcher.BeginInvoke(() => accept.Focus(), System.Windows.Threading.DispatcherPriority.Input);
        return answer.Task;

        void Escape(object sender, KeyEventArgs args)
        {
            if (args.Key != Key.Escape)
                return;
            args.Handled = true;
            Finish(false);
        }

        void Finish(bool result)
        {
            ConfirmBar.PreviewKeyDown -= Escape;
            if (pendingConfirm == answer)
            {
                pendingConfirm = null;
                ConfirmBar.Visibility = Visibility.Collapsed;
                ConfirmBar.Child = null;
            }
            answer.TrySetResult(result);
        }
    }
}
