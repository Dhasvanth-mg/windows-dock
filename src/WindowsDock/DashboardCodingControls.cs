using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace WindowsDock;

public partial class DashboardWindow
{
    private readonly Dictionary<(AgentProvider Provider, string Id), CodingDraft> codingDrafts = [];
    private int activeCodingRequests;

    private sealed class CodingDraft
    {
        internal string Text = "", Model = "", Effort = "", Feedback = "";
        internal string? CopyId, CopyReply;
        internal bool UseCopy;
        internal CancellationTokenSource? Request;
        internal Action? Changed;
    }

    private void BuildCodexConnectionSettings(StackPanel content, CancellationToken token)
    {
        var saved = CodingConnection.Load();
        var address = Field("Codex app-server address", 256);
        address.Tag = "ws://127.0.0.1:4500";
        address.Text = saved?.Address.ToString() ?? "";
        var secret = new PasswordBox { MaxLength = 4096, ToolTip = "Optional app-server bearer token" };
        AutomationProperties.SetName(secret, "Codex app-server bearer token");
        var feedback = Text(saved == null ? "" : "Saved connection", 12, AssistantSecondary);
        feedback.TextWrapping = TextWrapping.Wrap;
        var connect = ActionButton("Connect");
        var forget = QuietButton("Forget connection", () =>
        {
            try
            {
                CodingConnection.Forget();
                saved = null;
                address.Clear();
                secret.Clear();
                feedback.Text = "Connection removed.";
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { feedback.Text = "Couldn't remove the saved connection."; }
        });
        connect.Click += async (_, _) =>
        {
            connect.IsEnabled = forget.IsEnabled = false;
            using var check = CancellationTokenSource.CreateLinkedTokenSource(token);
            check.CancelAfter(TimeSpan.FromSeconds(8));
            try
            {
                var connection = CodingConnection.Parse(address.Text, secret.Password);
                if (connection.Bearer.Length == 0 && saved?.Address == connection.Address)
                    connection = connection with { Bearer = saved.Bearer };
                feedback.Text = "Connecting…";
                await CodingSessionControls.ProbeAsync(connection, check.Token);
                token.ThrowIfCancellationRequested();
                connection.Save();
                saved = connection;
                secret.Clear();
                feedback.Text = "Connected. Open Controls on a chat hosted by this app server.";
            }
            catch (OperationCanceledException) { if (!token.IsCancellationRequested) feedback.Text = "Connection timed out."; }
            catch (Exception error) when (error is ArgumentException or CodingControlException) { feedback.Text = error.Message; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException)
            { feedback.Text = "Couldn't save the connection."; }
            finally { connect.IsEnabled = forget.IsEnabled = true; }
        };
        content.Children.Add(SectionTitle("Shared Codex connection"));
        var help = Text("Use the local WebSocket address of the app server hosting the chats you want to control.", 12, AssistantSecondary);
        help.TextWrapping = TextWrapping.Wrap;
        content.Children.Add(help);
        content.Children.Add(Labeled("App-server address", address));
        content.Children.Add(Labeled("Bearer token (optional)", secret));
        content.Children.Add(new WrapPanel { Children = { connect, forget } });
        content.Children.Add(feedback);
    }

    private sealed partial class CodingWorkspace
    {
        private void BuildControls(StackPanel content, AgentSession selected, CancellationToken viewToken)
        {
            var key = (selected.Provider, selected.Id);
            var connection = selected.Provider == AgentProvider.Codex ? CodingConnection.Load() : null;
            if (!dock.codingDrafts.TryGetValue(key, out var draft))
            {
                draft = new CodingDraft { UseCopy = connection == null, CopyId = CodingSessionCopies.SavedId(selected) };
                dock.codingDrafts[key] = draft;
            }

            var live = connection != null && !draft.UseCopy;
            CodingLiveState? state = null;
            var checking = false;
            var lookedUpModels = false;
            string? checkMessage = null;
            var audience = dock.Text("", 12, AssistantSecondary);
            audience.TextWrapping = TextWrapping.Wrap;
            var feedback = dock.Text("", 12, AssistantSecondary);
            feedback.TextWrapping = TextWrapping.Wrap;
            FrameworkElement? mode = null;
            // The live or saved-copy switch goes first on screen, but it is built once the controls its handler updates
            // exist (below), so it gets a placeholder here.
            var modeHost = new ContentControl { Focusable = false };
            if (connection != null)
                content.Children.Add(modeHost);
            content.Children.Add(audience);

            var model = new ComboBox { IsEditable = true, IsTextSearchEnabled = false, Text = draft.Model, MaxDropDownHeight = 240 };
            model.ItemsSource = selected.Provider == AgentProvider.Claude
                ? new[] { "sonnet", "opus", "haiku" } : selected.Model is { } observed ? new[] { observed } : Array.Empty<string>();
            model.Text = draft.Model;
            AutomationProperties.SetName(model, "Coding model");
            model.LostKeyboardFocus += (_, _) => draft.Model = model.Text;
            var efforts = new[] { "Current" }.Concat(selected.Provider == AgentProvider.Codex
                ? CodingSessionControls.CodexEfforts : CodingSessionControls.ClaudeEfforts).ToArray();
            var effort = new ComboBox { ItemsSource = efforts, SelectedIndex = Math.Max(0, Array.IndexOf(efforts, draft.Effort)) };
            AutomationProperties.SetName(effort, "Coding thinking effort");
            effort.SelectionChanged += (_, _) => draft.Effort = effort.SelectedIndex > 0 ? efforts[effort.SelectedIndex] : "";
            var choices = new Grid();
            choices.ColumnDefinitions.Add(new() { Width = new GridLength(2, GridUnitType.Star) });
            choices.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            var modelField = dock.Labeled(selected.Model == null ? "Model ID (optional)" : "Model (current: " + selected.Model + ")", model);
            modelField.Margin = new Thickness(0, 0, 8, 0);
            choices.Children.Add(modelField);
            var effortField = dock.Labeled("Thinking", effort);
            Grid.SetColumn(effortField, 1);
            choices.Children.Add(effortField);
            content.Children.Add(choices);

            var prompt = dock.Field("Message to coding chat", CodingSessionControls.DraftLimit);
            prompt.Tag = "Write a follow-up";
            prompt.AcceptsReturn = true;
            prompt.TextWrapping = TextWrapping.Wrap;
            prompt.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            prompt.Height = 72;
            prompt.Text = draft.Text;
            content.Children.Add(prompt);
            var send = dock.ActionButton("Send");
            dock.MarkPrimary(send, true);
            var apply = dock.QuietButton("Apply model and thinking");
            var stop = dock.QuietButton("Stop turn");
            var cancel = dock.QuietButton("Cancel copy");
            var refresh = dock.QuietButton("Refresh");
            var setup = dock.QuietButton("Connect Codex");
            content.Children.Add(new WrapPanel { Children = { send, apply, stop, cancel, refresh, setup } });
            content.Children.Add(feedback);
            var response = dock.Field("Latest response from the saved copy", 0);
            response.IsReadOnly = true;
            response.TextWrapping = TextWrapping.Wrap;
            response.Margin = new Thickness(0, 8, 0, 0);
            content.Children.Add(response);

            // Handlers are attached only now, once every control they repaint exists.
            apply.Click += (_, _) => _ = RequestAsync("apply");
            stop.Click += (_, _) => _ = RequestAsync("stop");
            cancel.Click += (_, _) => draft.Request?.Cancel();
            refresh.Click += (_, _) => _ = RefreshLiveAsync();
            setup.Click += (_, _) => Navigate(Pane.Setup);
            if (connection != null)
            {
                mode = dock.Segmented("Coding conversation destination", ["Live chat", "Saved copy"], live ? 0 : 1, index =>
                {
                    live = index == 0;
                    draft.UseCopy = !live;
                    draft.Feedback = "";
                    Paint();
                    if (live)
                        _ = RefreshLiveAsync();
                }, out _);
                modeHost.Content = mode;
            }

            send.Click += (_, _) => _ = RequestAsync("send");
            prompt.TextChanged += (_, _) =>
            {
                if (prompt.Text == draft.Text)
                    return;
                draft.Text = prompt.Text;
                Paint();
            };
            prompt.PreviewKeyDown += (_, args) =>
            {
                if (args.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control && send.IsEnabled)
                {
                    args.Handled = true;
                    _ = RequestAsync("send");
                }
            };
            draft.Changed += Paint;
            viewToken.Register(() => draft.Changed -= Paint);
            Paint();
            if (live)
                _ = RefreshLiveAsync();
            if (draft.CopyId is { } id && draft.CopyReply == null)
                _ = ReadCopyReplyAsync(id);

            void Paint()
            {
                var busy = draft.Request != null;
                audience.Text = live ? checkMessage ?? (state?.Status switch
                {
                    "idle" => "Connected. Ready for a follow-up.",
                    "active" when state.NeedsApproval => "Connected. The turn is waiting for approval.",
                    "active" when state.ActiveTurn != null => "Connected. A turn is running.",
                    "active" => "The server couldn't identify the running turn.",
                    "notLoaded" => "Open this chat on the connected app server to control it here.",
                    _ => "Checking this chat…"
                }) : draft.CopyId == null ? "Continue from this chat's saved history in a separate, text-only conversation." :
                    "Send to the saved copy of this chat.";
                send.Content = live ? state?.ActiveTurn == null ? "Send follow-up" : "Steer this turn" :
                    draft.CopyId == null ? "Create copy and send" : "Send to copy";
                if (prompt.Text != draft.Text)
                    prompt.Text = draft.Text;
                send.IsEnabled = !busy && !checking && !string.IsNullOrWhiteSpace(prompt.Text) && (!live || state?.CanSend == true);
                apply.IsEnabled = !busy && !checking && state?.CanSend == true;
                stop.IsEnabled = !busy && !checking && state?.ActiveTurn != null;
                refresh.IsEnabled = !busy && !checking;
                foreach (var button in new[] { apply, stop, refresh })
                    button.Visibility = live ? Visibility.Visible : Visibility.Collapsed;
                cancel.Visibility = busy && !live ? Visibility.Visible : Visibility.Collapsed;
                setup.Visibility = selected.Provider == AgentProvider.Codex && connection == null && !busy ? Visibility.Visible : Visibility.Collapsed;
                if (mode != null)
                    mode.IsEnabled = !busy && !checking;
                prompt.IsReadOnly = busy;
                model.IsEnabled = effort.IsEnabled = !busy;
                feedback.Text = draft.Feedback;
                feedback.Visibility = draft.Feedback.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
                if (response.Text != (draft.CopyReply ?? ""))
                    response.Text = draft.CopyReply ?? "";
                response.Visibility = live || response.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            }

            async Task RefreshLiveAsync()
            {
                if (connection == null || checking || viewToken.IsCancellationRequested)
                    return;
                checking = true;
                checkMessage = null;
                Paint();
                using var check = CancellationTokenSource.CreateLinkedTokenSource(viewToken);
                check.CancelAfter(TimeSpan.FromSeconds(8));
                try
                {
                    state = await CodingSessionControls.InspectAsync(connection, selected.Id, check.Token);
                    if (state.Status is not ("idle" or "active" or "notLoaded"))
                        checkMessage = "The app server reports this chat is unavailable.";
                    if (!lookedUpModels)
                    {
                        lookedUpModels = true;
                        try
                        {
                            var available = await CodingSessionControls.ModelsAsync(connection, check.Token);
                            if (!viewToken.IsCancellationRequested && available.Length > 0)
                            {
                                var typed = model.Text;
                                model.ItemsSource = available;
                                model.Text = typed;
                            }
                        }
                        catch (CodingControlException) { }
                    }
                }
                catch (OperationCanceledException) { if (state == null) checkMessage = "The app server didn't respond."; }
                catch (CodingControlException error) { state = null; checkMessage = error.Message; }
                finally
                {
                    checking = false;
                    if (!viewToken.IsCancellationRequested)
                        Paint();
                }
            }

            async Task RequestAsync(string action)
            {
                if (draft.Request != null || checking || viewToken.IsCancellationRequested)
                    return;
                draft.Model = model.Text;
                draft.Text = prompt.Text;
                var targetTurn = state?.ActiveTurn;
                var useLive = live;
                using var request = CancellationTokenSource.CreateLinkedTokenSource(dock.lifetime.Token);
                request.CancelAfter(TimeSpan.FromSeconds(useLive ? 16 : 165));
                draft.Request = request;
                dock.activeCodingRequests++;
                draft.Feedback = useLive ? "Contacting Codex…" : "Waiting for the copy to reply…";
                draft.Changed?.Invoke();
                try
                {
                    if (!useLive)
                    {
                        draft.CopyReply = await CodingSessionCopies.SendAsync(selected, draft.Text, draft.Model, draft.Effort, request.Token);
                        draft.Feedback = "Saved copy replied.";
                    }
                    else if (action == "stop" && targetTurn != null)
                    {
                        await CodingSessionControls.StopAsync(connection!, selected.Id, targetTurn, request.Token);
                        draft.Feedback = "Stop requested.";
                    }
                    else if (action == "apply")
                    {
                        await CodingSessionControls.ApplyAsync(connection!, selected.Id, targetTurn, draft.Model, draft.Effort, request.Token);
                        draft.Feedback = targetTurn == null ? "Settings applied to this chat." : "Settings applied to this turn.";
                    }
                    else
                    {
                        await CodingSessionControls.SendAsync(connection!, selected.Id, targetTurn, draft.Text, draft.Model, draft.Effort, request.Token);
                        draft.Feedback = "Delivered to the live chat.";
                    }
                    if (action == "send")
                        draft.Text = "";
                }
                catch (OperationCanceledException)
                { draft.Feedback = useLive ? "The request timed out. Check the live chat before retrying." : "The copy stopped. Your draft is still here."; }
                catch (Exception error) when (error is ArgumentException or CodingControlException or CodingSummaryException)
                { draft.Feedback = error.Message; }
                catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
                { draft.Feedback = "Couldn't finish this request. Your draft is still here."; }
                finally
                {
                    draft.Request = null;
                    dock.activeCodingRequests--;
                    if (!useLive)
                        draft.CopyId = CodingSessionCopies.SavedId(selected);
                    if (dock.releaseWhenIdle && !dock.closed && !dock.IsVisible && !dock.HasActiveWork)
                        dock.idleExit.Start();
                    draft.Changed?.Invoke();
                    if (useLive && !viewToken.IsCancellationRequested)
                        _ = RefreshLiveAsync();
                }
            }

            async Task ReadCopyReplyAsync(string id)
            {
                try
                {
                    var copy = selected with { Id = id, DesktopId = null, LastReplyId = null, LastReplySessionId = null };
                    var reply = await dock.ReadAgentReplyAsync(copy, viewToken);
                    if (draft.CopyReply == null && !viewToken.IsCancellationRequested)
                    {
                        draft.CopyReply = reply?.Text;
                        Paint();
                    }
                }
                catch (OperationCanceledException) { }
            }
        }
    }
}
