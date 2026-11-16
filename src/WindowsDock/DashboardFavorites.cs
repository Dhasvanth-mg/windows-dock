using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace WindowsDock;

internal enum FavoriteDestination { Link, Workspace, Search, Assistant, Widget }

internal sealed record DockFavorite(Guid Id, string Name, string Folder, FavoriteDestination Kind, string Target);

internal static class DockFavorites
{
    private const string FileName = "favorites.json";
    internal static DockFavorite[] Read() => DockData.Read<DockFavorite[]>(FileName) ?? [];

    internal static void Save(DockFavorite favorite)
    {
        Validate(favorite);
        DockData.Update<DockFavorite[]>(FileName, saved =>
        {
            var entries = saved ?? [];
            if (!entries.Any(item => item.Id == favorite.Id) && entries.Length >= 300)
                throw new InvalidOperationException("Remove a favorite before saving more than 300.");
            return entries.Where(item => item.Id != favorite.Id).Append(favorite).ToArray();
        });
    }

    internal static void Remove(Guid id) => DockData.Update<DockFavorite[]>(FileName,
        saved => saved?.Where(item => item.Id != id).ToArray() ?? []);

    internal static void Validate(DockFavorite favorite)
    {
        if (!Enum.IsDefined(favorite.Kind) || favorite.Name.Length is < 1 or > 90 || favorite.Folder.Length > 70 ||
            favorite.Target.Length is < 1 or > 2048 || favorite.Name.Any(char.IsControl) || favorite.Folder.Any(char.IsControl) ||
            favorite.Target.Any(char.IsControl))
            throw new ArgumentException("Enter a name and a destination.");
        if (favorite.Kind == FavoriteDestination.Link && !IsLink(favorite.Target))
            throw new ArgumentException("Enter a local file or folder, or an HTTPS address.");
        if (favorite.Kind == FavoriteDestination.Assistant && !AssistantPersonas.All.Any(item => item.Id == favorite.Target))
            throw new ArgumentException("Choose an assistant.");
        if (favorite.Kind == FavoriteDestination.Widget)
            _ = DockWidgetLayout.WidgetId(favorite.Target);
    }

    internal static bool IsLink(string target) =>
        Path.IsPathFullyQualified(target) && !target.StartsWith(@"\\", StringComparison.Ordinal) && target.Length > 2 &&
            !target[2..].Contains(':') && (File.Exists(target) || Directory.Exists(target)) ||
        Uri.TryCreate(target, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
            uri.Host.Length > 0 && uri.UserInfo.Length == 0;
}

public partial class DashboardWindow
{
    private void ShowFavoritesWidget()
    {
        var (window, content, _) = CreateDockPanel("Favorites", "", 480, 0xE734, accent, widget: true);
        BuildPinnedFavorites(content);
        window.Show();
    }

    private void BuildPinnedFavorites(StackPanel content)
    {
        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var create = ActionButton("Add favorite");
        var filter = Field("Search favorites", 120);
        filter.Tag = "Search favorites";
        DockPanel.SetDock(create, Dock.Right);
        create.Margin = new Thickness(6, 0, 0, 0);
        bar.Children.Add(create);
        bar.Children.Add(filter);
        content.Children.Add(bar);
        var status = Text("", 11, AssistantSecondary);
        status.TextWrapping = TextWrapping.Wrap;
        content.Children.Add(status);
        var editor = new StackPanel { Visibility = Visibility.Collapsed };
        content.Children.Add(editor);
        var results = new StackPanel { Margin = new Thickness(0, 6, 0, 0), AllowDrop = true };
        content.Children.Add(results);
        create.Click += (_, _) => Edit(null);
        filter.TextChanged += (_, _) => Render();
        results.Drop += (_, args) =>
        {
            if (args.Data.GetData(DataFormats.FileDrop) is not string[] paths)
                return;
            Commit(() =>
            {
                foreach (var path in paths.Where(DockFavorites.IsLink).Take(20))
                {
                    var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
                    if (name.Length == 0)
                        name = path;
                    DockFavorites.Save(new(Guid.NewGuid(), name[..Math.Min(name.Length, 90)], "", FavoriteDestination.Link, path));
                }
            });
            args.Handled = true;
        };
        Render();

        void Render()
        {
            results.Children.Clear();
            var query = filter.Text.Trim();
            var groups = DockFavorites.Read().Where(item => (item.Name + " " + item.Folder + " " + item.Target)
                .Contains(query, StringComparison.OrdinalIgnoreCase)).OrderBy(item => item.Folder, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).Take(60).GroupBy(item => item.Folder);
            foreach (var group in groups)
            {
                if (group.Key.Length > 0)
                    results.Children.Add(Text(group.Key, 12, AssistantSecondary, true));
                foreach (var item in group)
                {
                    var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                    var change = IconButton(0xE70F, "Edit " + item.Name, () => Edit(item));
                    DockPanel.SetDock(change, Dock.Right);
                    row.Children.Add(change);
                    var go = ActionButton(item.Name, () => ActivateFavorite(item, status));
                    go.HorizontalContentAlignment = HorizontalAlignment.Left;
                    row.Children.Add(go);
                    results.Children.Add(row);
                }
            }
            if (results.Children.Count == 0)
                results.Children.Add(Text(query.Length == 0 ? "Add a favorite or drop a local file here." : "No matching favorites.",
                    11, AssistantSecondary));
        }

        void Edit(DockFavorite? current)
        {
            editor.Children.Clear();
            editor.Visibility = Visibility.Visible;
            var name = AddField("Favorite name", current?.Name, 90);
            var folder = AddField("Folder label", current?.Folder, 70);
            var kind = new ComboBox { ItemsSource = Enum.GetValues<FavoriteDestination>(), SelectedItem = current?.Kind ?? FavoriteDestination.Link };
            AutomationProperties.SetName(kind, "Destination type");
            editor.Children.Add(kind);
            var target = AddField("Destination", current?.Target, 2048);
            var choices = new ComboBox { DisplayMemberPath = nameof(FavoriteChoice.Label) };
            AutomationProperties.SetName(choices, "Choose destination");
            editor.Children.Add(choices);
            kind.SelectionChanged += (_, _) => UpdateTargets();
            UpdateTargets();
            var commands = new WrapPanel { Margin = new Thickness(0, 5, 0, 7) };
            commands.Children.Add(ActionButton("Save favorite", () =>
            {
                var destination = (FavoriteDestination)kind.SelectedItem;
                var value = choices.Visibility == Visibility.Visible ? (choices.SelectedItem as FavoriteChoice)?.Value ?? "" : target.Text.Trim();
                Commit(() => DockFavorites.Save(new(current?.Id ?? Guid.NewGuid(), name.Text.Trim(), folder.Text.Trim(), destination, value)));
            }));
            commands.Children.Add(ActionButton("Cancel", () => editor.Visibility = Visibility.Collapsed));
            if (current != null)
                commands.Children.Add(ActionButton("Remove favorite", () => Commit(() => DockFavorites.Remove(current.Id))));
            editor.Children.Add(commands);
            name.Focus();

            void UpdateTargets()
            {
                var list = ((FavoriteDestination)kind.SelectedItem) switch
                {
                    FavoriteDestination.Assistant => AssistantPersonas.All.Select(item => new FavoriteChoice(item.Name, item.Id)).ToArray(),
                    FavoriteDestination.Workspace => Workspaces().Select(item => new FavoriteChoice(item.Name, item.Name)).ToArray(),
                    FavoriteDestination.Widget => WidgetActions().Where(item => item.Id != "favorites")
                        .Select(item => new FavoriteChoice(item.Label, item.Id)).ToArray(),
                    _ => null
                };
                target.Visibility = list == null ? Visibility.Visible : Visibility.Collapsed;
                choices.Visibility = list == null ? Visibility.Collapsed : Visibility.Visible;
                choices.ItemsSource = list;
                choices.SelectedItem = list?.FirstOrDefault(item => item.Value == current?.Target);
            }
        }

        TextBox AddField(string label, string? value, int limit)
        {
            var field = Field(label, limit);
            field.Tag = label;
            field.Text = value ?? "";
            editor.Children.Add(field);
            return field;
        }

        void Commit(Action change)
        {
            try
            {
                change();
                editor.Visibility = Visibility.Collapsed;
                status.Text = "";
                Render();
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
            { status.Text = error.Message; Render(); }
        }
    }

    private sealed record FavoriteChoice(string Label, string Value);

    private void ActivateFavorite(DockFavorite favorite, TextBlock status)
    {
        try
        {
            DockFavorites.Validate(favorite);
            switch (favorite.Kind)
            {
                case FavoriteDestination.Link:
                    OpenTarget(favorite.Target);
                    break;
                case FavoriteDestination.Search:
                    ShowCommandPalette(favorite.Target);
                    break;
                case FavoriteDestination.Assistant:
                    OpenAssistantConversation(Array.FindIndex(AssistantPersonas.All, item => item.Id == favorite.Target));
                    break;
                case FavoriteDestination.Widget:
                    WidgetActions().First(widget => widget.Id.Equals(favorite.Target, StringComparison.OrdinalIgnoreCase)).Action();
                    break;
                case FavoriteDestination.Workspace:
                    var workspace = Workspaces().Where(item => item.Name == favorite.Target).ToArray();
                    if (workspace.Length != 1)
                        throw new InvalidOperationException("Edit this favorite and choose a unique workspace.");
                    _ = RestoreAsync(workspace[0]);
                    break;
            }
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or Win32Exception)
        { status.Text = error.Message; }

        async Task RestoreAsync(WorkspaceShortcut workspace)
        {
            status.Text = "Restoring " + workspace.Name + "…";
            var failed = await LaunchWorkspaceAsync(workspace);
            status.Text = failed == 0 ? "Workspace opened." : $"{failed} item(s) could not be restored.";
        }
    }
}
