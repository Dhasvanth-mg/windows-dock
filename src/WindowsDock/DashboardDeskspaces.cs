using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace WindowsDock;

public partial class DashboardWindow
{
    private void ShowDeskspaces() => ShowDeskspaces(null);

    private void ShowDeskspaces(Guid? selected)
    {
        var (window, content, token) = CreateDockPanel("Deskspace", "", 760, 0xE8B7, accent,
            widget: true, height: 620, expandable: true);
        var choice = new ComboBox { DisplayMemberPath = nameof(Deskspace.Name) };
        AutomationProperties.SetName(choice, "Deskspace to open");
        var name = Field("New deskspace name", 100);
        name.Tag = "Name your deskspace";
        var status = Text("", 11, AssistantSecondary);
        status.TextWrapping = TextWrapping.Wrap;
        var details = new StackPanel();
        var player = new MediaPlayer();
        token.Register(() => window.Dispatcher.InvokeAsync(player.Close));
        window.IsVisibleChanged += (_, _) => { if (!window.IsVisible) player.Close(); };
        content.Children.Add(choice);
        content.Children.Add(Labeled("Create a deskspace", name));
        content.Children.Add(ActionButton("Create deskspace", () => Run(() => Reload(DockDeskspaces.Add(name.Text).Id))));
        content.Children.Add(status);
        content.Children.Add(details);
        choice.SelectionChanged += (_, _) => Render();
        Reload(selected);
        window.Show();

        void Reload(Guid? id)
        {
            var items = DockDeskspaces.Spaces();
            choice.ItemsSource = items;
            choice.SelectedItem = items.FirstOrDefault(item => item.Id == id) ?? items.FirstOrDefault();
            Render();
        }

        void Run(Action action)
        {
            try { action(); status.Text = ""; }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
            { status.Text = error.Message; }
        }

        void Render()
        {
            details.Children.Clear();
            if (choice.SelectedItem is not Deskspace desk)
                return;
            var top = new WrapPanel { Margin = new Thickness(0, 8, 0, 6) };
            top.Children.Add(ActionButton("Restore windows and webpages", async () =>
            {
                try
                {
                    var current = DockDeskspaces.Get(desk.Id);
                    var missed = await LaunchWorkspaceAsync(new(current.Name, "", current.Links.Select(item => item.Url).ToArray(), current.Windows));
                    if (!token.IsCancellationRequested)
                        status.Text = missed == 0 ? "Deskspace restored." : $"{missed} saved item(s) could not be restored.";
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    if (!token.IsCancellationRequested)
                        status.Text = error.Message;
                }
            }));
            top.Children.Add(QuietButton("Delete deskspace", () =>
            {
                if (MessageBox.Show(DialogOwner(window), $"Remove {desk.Name}, its People networks, and its canvases?", "Deskspace",
                    MessageBoxButton.YesNo, MessageBoxImage.None, MessageBoxResult.No) == MessageBoxResult.Yes)
                    Run(() => { DockDeskspaces.Remove(desk.Id); Reload(null); });
            }));
            details.Children.Add(top);
            var identity = Field("Deskspace name", 100);
            identity.Text = desk.Name;
            var setup = new StackPanel { Children = { SectionTitle("Windows"), identity,
                QuietButton("Save deskspace name", () => Edit(item => item with { Name = DockDeskspaces.Label(identity.Text) })),
                Text($"{desk.Windows.Length} saved", 11, AssistantSecondary) } };
            setup.Children.Add(WorkspaceWindowPicker(desk.Windows, windows => Edit(item => item with { Windows = windows })));
            setup.Children.Add(QuietButton("Clear saved windows", () => Edit(item => item with { Windows = [] })));

            var web = new StackPanel { Children = { SectionTitle("Webpages") } };
            foreach (var page in desk.Links)
            {
                var saved = DeskRow(ActionButton(page.Title, () => OpenTarget(page.Url)),
                    () => Edit(item => item with { Links = item.Links.Where(link => link.Url != page.Url).ToArray() }), "Remove webpage " + page.Title);
                web.Children.Add(saved);
            }
            var url = Field("Webpage URL", 2048);
            url.Tag = "https://";
            var label = Field("Webpage label", 100);
            label.Tag = "Label (optional)";
            web.Children.Add(url);
            web.Children.Add(label);
            web.Children.Add(ActionButton("Add webpage", () =>
            {
                Edit(item =>
                {
                    var link = DockDeskspaces.Link(label.Text, url.Text);
                    return item with { Links = [.. item.Links.Where(saved => saved.Url != link.Url), link] };
                });
            }));

            var boards = new StackPanel { Children = { SectionTitle("People networks") } };
            foreach (var map in desk.Maps)
                boards.Children.Add(ActionButton(map.Name, () => ShowPeopleNetwork(desk.Id, map.Id)));
            var mapName = Field("New People network name", 100);
            mapName.Tag = "Network name";
            boards.Children.Add(mapName);
            boards.Children.Add(ActionButton("Create People network", () => Run(() => ShowPeopleNetwork(desk.Id, DockDeskspaces.AddMap(desk.Id, mapName.Text).Id))));
            boards.Children.Add(QuietButton("People directory", () => ShowDeskPeople(desk.Id)));
            var canvases = new StackPanel { Children = { SectionTitle("Project canvases") } };
            foreach (var canvas in desk.Canvases ?? [])
                canvases.Children.Add(ActionButton(canvas.Name, () => ShowProjectCanvas(desk.Id, canvas.Id)));
            var canvasName = Field("New project canvas name", 100);
            canvasName.Tag = "Canvas name";
            canvases.Children.Add(canvasName);
            canvases.Children.Add(ActionButton("Create project canvas", () => Run(() =>
                ShowProjectCanvas(desk.Id, DockDeskspaces.AddCanvas(desk.Id, canvasName.Text).Id))));

            var notes = new StackPanel { Children = { SectionTitle("Sticky notes") } };
            var knownNotes = DockNotes.Load().Where(item => item.Kind == DockNotes.Sticky).ToArray();
            foreach (var id in desk.Notes)
            {
                var existing = knownNotes.FirstOrDefault(item => item.Id == id);
                var row = DeskRow(ActionButton(existing == null ? "Missing note" : DockNotes.Title(existing), () =>
                {
                    if (existing != null)
                        ShowDeskNote(desk.Id, id);
                }), () => Edit(item => item with { Notes = item.Notes.Where(note => note != id).ToArray() }), "Unlink sticky note");
                notes.Children.Add(row);
            }
            var noteOptions = new ComboBox { ItemsSource = knownNotes.Where(item => !desk.Notes.Contains(item.Id))
                .Select(item => (Id: item.Id, Name: DockNotes.Title(item))).Select(item => new DeskResource(item.Id, item.Name)).ToArray(),
                DisplayMemberPath = nameof(DeskResource.Name), SelectedIndex = 0 };
            AutomationProperties.SetName(noteOptions, "Existing sticky notes");
            notes.Children.Add(noteOptions);
            notes.Children.Add(ActionButton("Add existing sticky", () =>
            {
                if (noteOptions.SelectedItem is DeskResource entry)
                    Edit(item => item with { Notes = item.Notes.Append(entry.Id).Distinct().ToArray() });
            }));
            notes.Children.Add(ActionButton("New sticky", () => Run(() =>
            {
                var note = DockNotes.Add(DockNotes.Sticky);
                DockDeskspaces.Change(desk.Id, item => item with { Notes = [.. item.Notes, note.Id] });
                ShowDeskNote(desk.Id, note.Id);
            })));

            var voice = new StackPanel { Children = { SectionTitle("Voice memos") } };
            var recordings = DockVoiceMemos.Load();
            foreach (var id in desk.Voice)
            {
                var memo = recordings.FirstOrDefault(item => item.Id == id);
                voice.Children.Add(DeskRow(ActionButton(memo == null ? "Missing recording" : memo.Title, () =>
                {
                    if (memo == null)
                        return;
                    var path = DockVoiceMemos.PathFor(id);
                    if (!File.Exists(path)) { status.Text = "This recording is missing."; return; }
                    player.Open(new Uri(path));
                    player.Play();
                }), () => Edit(item => item with { Voice = item.Voice.Where(memo => memo != id).ToArray() }), "Unlink voice memo"));
            }
            var voiceOptions = new ComboBox { ItemsSource = recordings.Where(item => !desk.Voice.Contains(item.Id)).ToArray(),
                DisplayMemberPath = nameof(VoiceMemo.Title), SelectedIndex = 0 };
            AutomationProperties.SetName(voiceOptions, "Existing voice memos");
            voice.Children.Add(voiceOptions);
            voice.Children.Add(ActionButton("Add voice memo", () =>
            {
                if (voiceOptions.SelectedItem is VoiceMemo memo)
                    Edit(item => item with { Voice = item.Voice.Append(memo.Id).Distinct().ToArray() });
            }));
            voice.Children.Add(new WrapPanel { Children = { QuietButton("Stop audio", player.Close),
                QuietButton("Open Voice memos", ShowVoiceMemos) } });
            foreach (var part in new[] { boards, canvases, setup, web, notes, voice })
            {
                var card = Card(part);
                card.Margin = new Thickness(0, 0, 0, 6);
                details.Children.Add(card);
            }

            void Edit(Func<Deskspace, Deskspace> modify) => Run(() => { DockDeskspaces.Change(desk.Id, modify); Reload(desk.Id); });
        }
    }

    private sealed record DeskResource(Guid Id, string Name);

    private DockPanel DeskRow(Button open, Action unlink, string name)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var remove = IconButton(0xE711, name, unlink);
        DockPanel.SetDock(remove, Dock.Right);
        row.Children.Add(remove);
        open.HorizontalContentAlignment = HorizontalAlignment.Left;
        row.Children.Add(open);
        return row;
    }

    private void ShowDeskNote(Guid desk, Guid id)
    {
        if (DockNotes.Load().FirstOrDefault(item => item.Id == id) is not { } note)
            return;
        var (window, content, _) = CreateDockPanel("Deskspace sticky", "", 480, 0xE70B, amber,
            backAction: () => ShowDeskspaces(desk), backLabel: "Back to deskspace");
        var text = Field("Sticky note text", DockNotes.TextLimit);
        text.AcceptsReturn = true;
        text.TextWrapping = TextWrapping.Wrap;
        text.Height = 180;
        text.Text = note.Text;
        var feedback = Text("", 11, AssistantSecondary);
        content.Children.Add(text);
        content.Children.Add(ActionButton("Save sticky", () =>
        {
            try
            {
                DockNotes.Change(id, item => item with { Text = text.Text, Updated = DateTimeOffset.UtcNow });
                feedback.Text = "Sticky saved.";
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { feedback.Text = "Could not save this sticky."; }
        }));
        content.Children.Add(feedback);
        window.Show();
    }

    private void ShowDeskPeople(Guid desk, Guid? network = null, Guid? selected = null)
    {
        var (window, content, _) = CreateDockPanel("People directory", "", 540, 0xE716, accent,
            backAction: () => { if (network is { } map) ShowPeopleNetwork(desk, map); else ShowDeskspaces(desk); },
            backLabel: network == null ? "Back to deskspace" : "Back to network", height: 560);
        var people = new ComboBox { DisplayMemberPath = nameof(DeskPerson.Name), IsTextSearchEnabled = true };
        AutomationProperties.SetName(people, "People in your directory");
        var name = Field("Person name", 100);
        var email = Field("Person email", 320);
        var phone = Field("Person phone", 100);
        var site = Field("Person website", 2048);
        var notes = Field("Person notes", 8000);
        notes.AcceptsReturn = true;
        notes.TextWrapping = TextWrapping.Wrap;
        notes.Height = 120;
        var detail = Text("", 11, AssistantSecondary);
        detail.TextWrapping = TextWrapping.Wrap;
        var preview = new Image { Width = 100, Height = 100, Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Left };
        string? photo = null;
        content.Children.Add(people);
        content.Children.Add(ActionButton("New person", () => { people.SelectedIndex = -1; Populate(null); name.Focus(); }));
        var photoTools = new WrapPanel { Children =
        {
            QuietButton("Choose photo", () => ChangePhoto(() => DockDeskImages.Choose(window))),
            QuietButton("Paste photo", () => ChangePhoto(() => System.Windows.Clipboard.GetImage() is { } image
                ? DockDeskImages.Keep(image) : throw new ArgumentException("Copy an image first."))),
            QuietButton("Remove photo", () => SetPhoto(null))
        } };
        content.Children.Add(preview);
        content.Children.Add(photoTools);
        content.Children.Add(Labeled("Name", name));
        content.Children.Add(Labeled("Email", email));
        content.Children.Add(Labeled("Phone", phone));
        content.Children.Add(Labeled("Website", site));
        content.Children.Add(Labeled("Notes", notes));
        content.Children.Add(ActionButton("Save person", () =>
        {
            try
            {
                var person = DockDeskspaces.SavePerson((people.SelectedItem as DeskPerson)?.Id, name.Text, notes.Text,
                    email.Text, phone.Text, site.Text, photo);
                Refresh(person.Id);
            }
            catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
            { detail.Text = error.Message; }
        }));
        content.Children.Add(detail);
        people.SelectionChanged += (_, _) => Populate(people.SelectedItem as DeskPerson);
        Refresh(selected);
        window.Show();

        void ChangePhoto(Func<string?> pick)
        {
            try { if (pick() is { } image) SetPhoto(image); }
            catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException
                or System.Runtime.InteropServices.ExternalException)
            { detail.Text = error.Message; }
        }

        void SetPhoto(string? image)
        {
            photo = image;
            preview.Source = DockDeskImages.Open(image, 160);
            preview.Visibility = preview.Source == null ? Visibility.Collapsed : Visibility.Visible;
        }

        void Populate(DeskPerson? current)
        {
            name.Text = current?.Name ?? "";
            email.Text = current?.Email ?? "";
            phone.Text = current?.Phone ?? "";
            site.Text = current?.Website ?? "";
            notes.Text = current?.Notes ?? "";
            SetPhoto(current?.Photo);
            detail.Text = current == null ? "" : $"{DockDeskspaces.Spaces().SelectMany(item => item.Maps)
                .Count(map => map.Nodes.Any(node => node.Person == current.Id))} connected network(s)";
        }

        void Refresh(Guid? id)
        {
            var items = DockDeskspaces.People().OrderBy(item => item.Name).ToArray();
            people.ItemsSource = items;
            people.SelectedItem = items.FirstOrDefault(person => person.Id == id) ?? items.FirstOrDefault();
            Populate(people.SelectedItem as DeskPerson);
        }
    }
}
