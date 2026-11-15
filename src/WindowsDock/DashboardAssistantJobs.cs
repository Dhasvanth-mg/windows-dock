using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace WindowsDock;

public partial class DashboardWindow
{
    private const int JobCapacity = 2;
    private readonly Dictionary<Guid, CancellationTokenSource> runningJobs = new();
    private Action? updateJobsPanel;

    private void ShowAssistantJobs()
    {
        var (window, content, cancellation) = CreateDockPanel("Assistant jobs", "", 500, 0xE823, AssistantInk, widget: true);
        BuildPinnedJobs(window, content, cancellation);
        window.Show();
    }

    private void BuildPinnedJobs(Window window, StackPanel content, CancellationToken cancellation)
    {
        var tools = new WrapPanel { Margin = new Thickness(0, 0, 0, 7) };
        var code = ActionButton("New coding job", ComposeCodingJob);
        code.Margin = new Thickness(0, 0, 5, 0);
        tools.Children.Add(code);
        var orbit = ActionButton("Ask Orbit", () => OpenAssistantConversation(3));
        orbit.Margin = new Thickness(0, 0, 5, 0);
        tools.Children.Add(orbit);
        tools.Children.Add(IconButton(0xE72C, "Refresh jobs", () => updateJobsPanel?.Invoke()));
        content.Children.Add(tools);
        var rows = new StackPanel();
        content.Children.Add(rows);
        updateJobsPanel = Render;
        cancellation.Register(() => updateJobsPanel = null);
        Render();

        void Render()
        {
            var jobs = DockJobs.Load();
            rows.Children.Clear();
            foreach (var job in jobs)
            {
                var state = runningJobs.TryGetValue(job.Id, out var running) && running.IsCancellationRequested
                    ? "Canceling" : job.Status.ToString();
                var label = job.Task.Split('\n', 2)[0].Trim();
                if (label.Length > 50)
                    label = label[..49] + "…";
                var row = WidgetTile(new(label, $"{job.Specialist} · {state}", "", 0xE823, AssistantSecondary,
                    () => ShowJobDetails(job.Id)));
                row.Margin = new Thickness(0, 0, 0, 4);
                rows.Children.Add(row);
            }
            if (jobs.Length == 0)
                rows.Children.Add(Text("Jobs from Orbit and coding runs show up here.", 11, AssistantSecondary));
        }
    }

    private void ComposeCodingJob()
    {
        var (window, content, _) = CreateDockPanel("New coding job", "Bolt", 472, 0xE943, AssistantInk,
            backAction: ShowAssistantJobs, backLabel: "Back to jobs");
        var task = Field("Coding task", AssistantMessageLimit);
        task.Tag = "Describe what Bolt should change";
        task.AcceptsReturn = true;
        task.TextWrapping = TextWrapping.Wrap;
        task.Height = 80;
        content.Children.Add(task);
        var workspace = Text("Choose a local Git workspace.", 11, AssistantSecondary);
        workspace.TextWrapping = TextWrapping.Wrap;
        workspace.Margin = new Thickness(0, 8, 0, 6);
        content.Children.Add(workspace);
        string? directory = null;
        var status = Text("", 11, AssistantSecondary);
        status.TextWrapping = TextWrapping.Wrap;
        var choices = new WrapPanel();
        var choose = ActionButton("Choose project…", () =>
        {
            var folder = PickCodingWorkspace(window);
            if (folder != null)
            {
                directory = folder;
                workspace.Text = folder;
            }
        });
        choose.Margin = new Thickness(0, 0, 5, 0);
        choices.Children.Add(choose);
        choices.Children.Add(ActionButton("Start coding job", () =>
        {
            if (directory == null || string.IsNullOrWhiteSpace(task.Text))
            {
                status.Text = "Enter a task and select a local Git workspace.";
                return;
            }
            try
            {
                QueueAssistantJob(new("coding", task.Text.Trim()), directory, null);
                ShowAssistantJobs();
            }
            catch (Exception error) when (DockActions.IsActionError(error)) { status.Text = error.Message; }
        }));
        content.Children.Add(choices);
        content.Children.Add(status);
        window.Show();
    }

    private string? PickCodingWorkspace(Window parent)
    {
        var dialog = new OpenFolderDialog { Title = "Choose the local Git project for Bolt" };
        if (dialog.ShowDialog(parent) != true)
            return null;
        if (DockCodingJob.IsWorkspace(dialog.FolderName))
            return dialog.FolderName;
        MessageBox.Show(parent, "Choose the folder that contains the project's .git directory or file.", "Windows Dock");
        return null;
    }

    private void QueueAssistantJob(DockHandoff handoff, string? workspace,
        Func<string, IReadOnlyList<(bool User, string Text)>, CancellationToken, Task<string>>? send)
    {
        if (runningJobs.Count >= JobCapacity)
            throw new InvalidOperationException("Two jobs are already running. Cancel one or wait for it to finish.");
        if (handoff.Role == "coding" && (workspace == null || !DockCodingJob.IsWorkspace(workspace)))
            throw new ArgumentException("Choose the root folder of a local Git workspace.");
        if (handoff.Role != "coding" && send == null)
            throw new InvalidOperationException("Connect a provider before starting this job.");
        var job = new DockJob(Guid.NewGuid(), handoff.Role, handoff.Task, workspace, DateTimeOffset.Now,
            DockJobStatus.Running, Environment.ProcessId, "");
        DockJobs.Save(job);
        var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        runningJobs.Add(job.Id, request);
        updateJobsPanel?.Invoke();
        _ = RunAsync();

        async Task RunAsync()
        {
            try
            {
                var result = handoff.Role == "coding"
                    ? new DockAssistantReply(await Task.Run(() => DockCodingJob.RunAsync(workspace!, handoff.Task, request.Token), request.Token), [])
                    : await DockAssistantTurn.RunAsync(handoff.Role, handoff.Instructions, [(true, handoff.Task)], trayMode,
                        send!, _ => { }, request.Token);
                request.Token.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(result.Text))
                    throw new InvalidOperationException("The provider returned an empty result.");
                job = job with { Status = DockJobStatus.Completed, Result = result.Text[..Math.Min(result.Text.Length, 48_000)], Proposal = result.Proposal };
            }
            catch (OperationCanceledException) when (request.IsCancellationRequested)
            {
                job = job with { Status = DockJobStatus.Canceled, Result = "The job was canceled. A coding job may have left partial changes in its workspace." };
            }
            catch (Exception error) when (DockActions.IsActionError(error) ||
                error is TimeoutException or PlanException or CodingSummaryException or OperationCanceledException)
            {
                var message = error is OperationCanceledException ? "The provider timed out." : error.Message;
                job = job with { Status = DockJobStatus.Failed, Result = message[..Math.Min(message.Length, 600)] };
            }
            finally
            {
                try { DockJobs.Save(job); }
                catch (Exception error) when (DockActions.IsActionError(error)) { }
                runningJobs.Remove(job.Id);
                request.Dispose();
                updateJobsPanel?.Invoke();
                if (job.Status == DockJobStatus.Completed)
                {
                    DockCompletion.Signal(controllerPid);
                    UpdateCompletionCue();
                }
                if (releaseWhenIdle && !IsVisible && runningJobs.Count == 0)
                    idleExit.Start();
            }
        }
    }

    private void ShowJobDetails(Guid id)
    {
        var job = DockJobs.Load().FirstOrDefault(item => item.Id == id);
        if (job == null)
            return;
        var (window, content, token) = CreateDockPanel("Job details", job.Specialist, 506, 0xE823, AssistantInk,
            backAction: ShowAssistantJobs, backLabel: "Back to jobs");
        var task = Text(job.Task, 12, AssistantSecondary);
        task.TextWrapping = TextWrapping.Wrap;
        content.Children.Add(new ScrollViewer { Content = task, MaxHeight = 116, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        if (job.Workspace != null)
        {
            var workspace = Text(job.Workspace, 10, AssistantSecondary);
            workspace.TextWrapping = TextWrapping.Wrap;
            workspace.Margin = new Thickness(0, 5, 0, 0);
            content.Children.Add(workspace);
        }
        var status = Text(job.Status.ToString(), 11, AssistantSecondary, true);
        status.Margin = new Thickness(0, 8, 0, 5);
        content.Children.Add(status);
        var result = Field("Job result", 0);
        result.IsReadOnly = true;
        result.TextWrapping = TextWrapping.Wrap;
        result.MaxHeight = 280;
        result.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        result.Text = job.Result;
        if (job.Result.Length > 0)
            content.Children.Add(result);
        var actions = new WrapPanel { Margin = new Thickness(0, 7, 0, 0) };
        if (runningJobs.TryGetValue(job.Id, out var running))
        {
            var cancel = ActionButton("Cancel job", () =>
            {
                if (runningJobs.TryGetValue(job.Id, out var current))
                    current.Cancel();
                status.Text = "Canceling…";
            });
            cancel.IsEnabled = !running.IsCancellationRequested;
            actions.Children.Add(cancel);
            updateJobsPanel = () => { if (!runningJobs.ContainsKey(id)) ShowJobDetails(id); };
            token.Register(() => updateJobsPanel = null);
        }
        if (job.Result.Length > 0)
        {
            actions.Children.Add(ActionButton("Copy result", () =>
            {
                try { Clipboard.SetText(job.Result); status.Text = "Copied to clipboard."; }
                catch (System.Runtime.InteropServices.ExternalException) { status.Text = "The clipboard is busy."; }
            }));
            actions.Children.Add(ActionButton("Save as Markdown…", () =>
            {
                var dialog = new SaveFileDialog { Filter = "Markdown|*.md", FileName = $"dock-{job.Role}.md" };
                if (dialog.ShowDialog(window) != true)
                    return;
                try { File.WriteAllText(dialog.FileName, job.Result); status.Text = "Saved."; }
                catch (Exception error) when (DockActions.IsActionError(error)) { status.Text = error.Message; }
            }));
        }
        if (job.Proposal != null)
        {
            try
            {
                using var json = JsonDocument.Parse(job.Proposal);
                var planned = DockAssistantTurn.PlanActions(json.RootElement, trayMode);
                foreach (var action in planned)
                    content.Children.Add(Text(action.Preview, 12, AssistantInk));
                var apply = ActionButton("Apply changes");
                apply.Click += async (_, _) =>
                {
                    apply.IsEnabled = false;
                    try
                    {
                        job = job with { Proposal = null };
                        DockJobs.Save(job);
                        var applied = await RunDockChangesAsync(planned, token);
                        job = job with { Result = applied };
                        DockJobs.Save(job);
                        if (!token.IsCancellationRequested)
                            ShowJobDetails(id);
                    }
                    catch (Exception error) when (DockActions.IsActionError(error)) { status.Text = error.Message; }
                };
                actions.Children.Add(apply);
            }
            catch (Exception error) when (DockActions.IsActionError(error)) { status.Text = error.Message; }
        }
        content.Children.Add(actions);
        window.Show();
    }

    private static async Task<string> RunDockChangesAsync(IEnumerable<DockAction> actions, CancellationToken token)
    {
        var results = new List<string>();
        try
        {
            foreach (var action in actions)
            {
                token.ThrowIfCancellationRequested();
                try { results.Add(await action.Execute(token)); }
                catch (Exception error) when (DockActions.IsActionError(error))
                {
                    results.Add($"{action.Preview}: {error.Message}");
                }
            }
        }
        catch (OperationCanceledException) { results.Add("Remaining changes canceled."); }
        return string.Join("\n", results);
    }
}
