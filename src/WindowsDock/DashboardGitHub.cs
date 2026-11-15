using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace WindowsDock;

public partial class DashboardWindow
{
    private void ShowGitHubWidget()
    {
        var (window, content, cancellation) = CreateDockPanel("GitHub checks and reviews", "", 476, 0xE73E, purple, widget: true);
        BuildPinnedGitHub(content, cancellation);
        window.Show();
    }

    private void BuildPinnedGitHub(StackPanel content, System.Threading.CancellationToken cancellation)
    {
        var query = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var refresh = AccentButton("Refresh", 0xE72C, purple);
        DockPanel.SetDock(refresh, Dock.Right);
        refresh.Margin = new Thickness(6, 0, 0, 0);
        query.Children.Add(refresh);
        var repo = Field("GitHub repository", 200);
        repo.Tag = "owner/repository";
        repo.Text = ReadJson<string>("github-repository.json") ?? RepositoryForSelectedChat() ?? "";
        query.Children.Add(repo);
        content.Children.Add(query);
        var status = Text("", 11, subdued);
        status.TextWrapping = TextWrapping.Wrap;
        content.Children.Add(status);
        var results = new StackPanel();
        content.Children.Add(results);
        content.Children.Add(ActionButton("Open GitHub reviews", () => OpenTarget("https://github.com/pulls/review-requested")));
        refresh.Click += (_, _) => _ = LoadAsync();
        repo.KeyDown += (_, args) =>
        {
            if (args.Key == System.Windows.Input.Key.Enter && refresh.IsEnabled)
            {
                args.Handled = true;
                _ = LoadAsync();
            }
        };
        if (repo.Text.Length > 0)
            _ = LoadAsync();

        async System.Threading.Tasks.Task LoadAsync()
        {
            var value = repo.Text.Trim();
            if (!Regex.IsMatch(value, @"\A[A-Za-z0-9][A-Za-z0-9-]*/[A-Za-z0-9_.-]+\z", RegexOptions.CultureInvariant))
            {
                status.Text = "Enter a GitHub repository as owner/repository.";
                return;
            }
            var gh = FindGitHubCli();
            if (gh == null)
            {
                status.Text = "Install GitHub CLI and sign in to load checks and review requests.";
                return;
            }
            refresh.IsEnabled = false;
            repo.IsEnabled = false;
            status.Text = "Checking GitHub…";
            results.Children.Clear();
            try
            {
                var pullRequests = WidgetCommand.ReadAsync(gh,
                    ["pr", "status", "-R", "github.com/" + value, "--json", "number,title,url,isDraft,reviewDecision,statusCheckRollup"],
                    cancellation);
                var workflowRuns = WidgetCommand.ReadAsync(gh,
                    ["run", "list", "-R", "github.com/" + value, "--limit", "6", "--json",
                        "displayTitle,name,headBranch,status,conclusion,url"], cancellation);
                await System.Threading.Tasks.Task.WhenAll(pullRequests, workflowRuns);
                var output = await pullRequests;
                var workflows = await workflowRuns;
                if (cancellation.IsCancellationRequested)
                    return;
                if (output == null && workflows == null)
                {
                    status.Text = "GitHub CLI could not read this repository. Check its sign-in and access.";
                    return;
                }
                using var doc = JsonDocument.Parse(output ?? "{}");
                var count = Add("Review requested", "needsReview") + Add("Your pull requests", "createdBy");
                status.Text = output == null ? "Pull requests are unavailable for this repository." :
                    count == 0 ? "No open pull requests of yours or review requests in this repository." :
                    $"{count} pull request{(count == 1 ? "" : "s")}";
                if (workflows != null)
                {
                    using var workflowDocument = JsonDocument.Parse(workflows);
                    if (workflowDocument.RootElement.ValueKind == JsonValueKind.Array &&
                        workflowDocument.RootElement.GetArrayLength() > 0)
                    {
                        var heading = Text("Recent workflow runs", 11, subdued, true);
                        heading.Margin = new Thickness(0, 7, 0, 1);
                        results.Children.Add(heading);
                        foreach (var run in workflowDocument.RootElement.EnumerateArray())
                        {
                            var title = AgentSessions.String(run, "displayTitle") ?? AgentSessions.String(run, "name") ?? "Workflow";
                            var state = AgentSessions.String(run, "status");
                            var detail = state == "completed" ? AgentSessions.String(run, "conclusion") ?? state : state;
                            AddLink(run, title, string.Join(" · ", new[] { AgentSessions.String(run, "headBranch"), detail?.Replace('_', ' ') }
                                .Where(part => !string.IsNullOrWhiteSpace(part))));
                        }
                    }
                }
                WriteJson("github-repository.json", value);

                int Add(string name, string key)
                {
                    var requests = AgentSessions.Property(doc.RootElement, key);
                    if (requests.ValueKind != JsonValueKind.Array || requests.GetArrayLength() == 0)
                        return 0;
                    var heading = Text(name, 11, subdued, true);
                    heading.Margin = new Thickness(0, 7, 0, 1);
                    results.Children.Add(heading);
                    foreach (var request in requests.EnumerateArray().Take(10))
                    {
                        var title = AgentSessions.String(request, "title") ?? "Pull request";
                        var description = $"#{AgentSessions.Count(request, "number")} · {title}";
                        AddLink(request, description, CheckStatus(request));
                    }
                    return requests.GetArrayLength();
                }

                void AddLink(JsonElement entry, string title, string detail)
                {
                    var url = AgentSessions.String(entry, "url");
                    if (!Uri.TryCreate(url, UriKind.Absolute, out var address) || address.Scheme != Uri.UriSchemeHttps ||
                        address.Host != "github.com" || address.UserInfo.Length != 0)
                        return;
                    var body = CardHeading(title, 0xE73E, purple,
                        DockGlyph(0xE8A7, 12, purple), detail);
                    body.Margin = new Thickness(0);
                    var button = MakeButton(body, title, () => OpenTarget(address.AbsoluteUri));
                    button.ToolTip = title;
                    button.Margin = new Thickness(0, 3, 0, 0);
                    button.HorizontalAlignment = HorizontalAlignment.Stretch;
                    button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                    results.Children.Add(button);
                }
            }
            catch (OperationCanceledException) { }
            catch (JsonException) { status.Text = "GitHub CLI returned an unreadable response."; }
            finally { refresh.IsEnabled = repo.IsEnabled = true; }
        }
    }

    private static string CheckStatus(JsonElement request)
    {
        var rollup = AgentSessions.Property(request, "statusCheckRollup");
        var checks = rollup.ValueKind == JsonValueKind.Array ? rollup.EnumerateArray().ToArray() : [];
        var failed = checks.Count(check => AgentSessions.String(check, "conclusion") is "FAILURE" or "TIMED_OUT" or "ACTION_REQUIRED" ||
            AgentSessions.String(check, "state") is "FAILURE" or "ERROR");
        var pending = checks.Count(check => AgentSessions.String(check, "status") is "IN_PROGRESS" or "QUEUED" or "PENDING" or "WAITING" or "REQUESTED" ||
            AgentSessions.String(check, "state") is "PENDING" or "EXPECTED");
        var review = AgentSessions.String(request, "reviewDecision") switch
        {
            "APPROVED" => "Approved", "REVIEW_REQUIRED" => "Review required",
            "CHANGES_REQUESTED" => "Changes requested", _ => null
        };
        var status = failed > 0 ? $"{failed} failed" : pending > 0 ? $"{pending} running" :
            checks.Length == 0 ? "No checks" : checks.All(check => AgentSessions.String(check, "conclusion") is "SUCCESS" or "SKIPPED" or "NEUTRAL" ||
                AgentSessions.String(check, "state") == "SUCCESS") ? "Checks passed" : "Checks finished";
        return review == null ? status : $"{status} · {review}";
    }

    private string? RepositoryForSelectedChat()
    {
        if (selectedAgent is not { } id)
            return null;
        var session = agentSessions.FirstOrDefault(item => item.Provider == id.Provider && item.Id == id.Id);
        if (session?.ProjectPath is not { } project)
            return null;
        try
        {
            var config = Path.Combine(project, ".git", "config");
            if (!File.Exists(config) || new FileInfo(config).Length > 65_536)
                return null;
            var result = Regex.Match(File.ReadAllText(config), @"github\.com[:/]([A-Za-z0-9-]+/[A-Za-z0-9_.-]+)",
                RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
            if (!result.Success)
                return null;
            var repository = result.Groups[1].Value;
            return repository.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? repository[..^4] : repository;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    private static string? FindGitHubCli()
    {
        var standard = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GitHub CLI", "gh.exe");
        if (File.Exists(standard))
            return standard;
        foreach (var entry in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var folder = entry.Trim().Trim('"');
            if (Path.IsPathFullyQualified(folder) && File.Exists(Path.Combine(folder, "gh.exe")))
                return Path.Combine(folder, "gh.exe");
        }
        return null;
    }
}
