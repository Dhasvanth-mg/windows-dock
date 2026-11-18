using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace WindowsDock;

// Notes: a wall of stickies for things you would otherwise put on the monitor bezel, and memos for anything longer.
// One sticky also lives on Today. Everything saves by itself a moment after you stop typing.
public partial class DashboardWindow
{
    private static readonly SolidColorBrush paperInk = Brush("#1E1B14");
    private static readonly SolidColorBrush paperSoftInk = Brush("#8C1E1B14");
    private bool notesShowMemos;
    private Guid? openMemo;

    private UIElement NotesPage()
    {
        var page = FillPage(new DockPanel { LastChildFill = true });
        var bar = new DockPanel { Margin = new Thickness(0, -2, 0, 12) };
        var create = ActionButton(notesShowMemos ? "New memo" : "New sticky", () =>
        {
            try
            {
                var notes = DockNotes.Load();
                var paper = DockNotes.Papers[notes.Count(note => note.Kind == DockNotes.Sticky) % DockNotes.Papers.Length].Id;
                var note = DockNotes.Add(notesShowMemos ? DockNotes.Memo : DockNotes.Sticky, "", paper);
                if (notesShowMemos)
                    openMemo = note.Id;
                focusNote = note.Id;
                ShowPage(DashboardPage.Notes);
            }
            catch (Exception error) when (DockActions.IsActionError(error)) { ShowOwnedMessage(error.Message); }
        });
        DockPanel.SetDock(create, Dock.Right);
        bar.Children.Add(create);
        bar.Children.Add(Segmented("Kind of note", ["Stickies", "Memos"], notesShowMemos ? 1 : 0, index =>
        {
            notesShowMemos = index == 1;
            ShowPage(DashboardPage.Notes);
        }));
        DockPanel.SetDock(bar, Dock.Top);
        page.Children.Add(bar);
        page.Children.Add(notesShowMemos ? MemoDesk() : StickyWall());
        return page;
    }

    private Guid? focusNote;

    private FrameworkElement StickyWall()
    {
        var stickies = DockNotes.Load().Where(note => note.Kind == DockNotes.Sticky).OrderByDescending(note => note.Pinned)
            .ThenByDescending(note => note.Updated).ToArray();
        if (stickies.Length == 0)
        {
            var empty = Text("No stickies yet. Start one and it also shows up on Today.", 13, AssistantFaint);
            empty.Margin = new Thickness(0, 6, 0, 0);
            return empty;
        }
        var wall = new WrapPanel { Margin = new Thickness(-4, 0, 0, 0) };
        foreach (var note in stickies)
        {
            var card = StickyCard(note, 208, 138, wall: true);
            card.Margin = new Thickness(6, 4, 10, 12);
            wall.Children.Add(card);
        }
        return wall;
    }

    // A sticky is paper: its own colour, dark ink, and it sits a touch crooked on the wall.
    private FrameworkElement StickyCard(DockNote note, double width, double height, bool wall)
    {
        var paper = new Border
        {
            Background = Brush(DockNotes.PaperHex(note.Color)), CornerRadius = new CornerRadius(3), Height = height,
            Padding = new Thickness(10, 8, 8, 4)
        };
        if (!double.IsNaN(width))
            paper.Width = width;
        if (wall && SystemParameters.ClientAreaAnimation)
        {
            // The same note always leans the same way.
            var lean = (Math.Abs(note.Id.GetHashCode()) % 5 - 2) * .55;
            paper.RenderTransformOrigin = new Point(.5, .5);
            paper.RenderTransform = new RotateTransform(lean);
        }
        var layout = new DockPanel { LastChildFill = true };
        var footer = new DockPanel { Margin = new Thickness(-4, 0, -2, 0) };
        var text = new TextBox
        {
            Text = note.Text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Background = Brushes.Transparent,
            BorderThickness = new Thickness(0), Foreground = paperInk, CaretBrush = paperInk, SelectionBrush = paperSoftInk,
            FontSize = 13, MaxLength = DockNotes.TextLimit, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0)
        };
        AutomationProperties.SetName(text, "Sticky note");
        var save = SaveSoon(note.Id, () => text.Text);

        var recolor = PaperButton(0xE790, "Change the paper colour", () =>
        {
            save(true);
            Apply(saved => saved with { Color = DockNotes.NextPaper(saved.Color), Updated = saved.Updated });
        });
        footer.Children.Add(recolor);
        var pin = PaperButton(note.Pinned ? 0xE840 : 0xE718, note.Pinned ? "On Today. Click to take it off" : "Show this one on Today", () =>
        {
            save(true);
            Apply(saved => saved with { Pinned = !saved.Pinned });
        });
        footer.Children.Add(pin);
        var remove = PaperButton(0xE74D, "Throw this note away", async () =>
        {
            if (text.Text.Trim().Length > 0)
            {
                var wasModal = modalOpen;
                modalOpen = true;
                try
                {
                    if (!await ConfirmAsync(this, "Throw this note away? It cannot be brought back.", "Throw away"))
                        return;
                }
                finally { modalOpen = wasModal; }
            }
            save(false);
            Apply(_ => null);
        });
        DockPanel.SetDock(remove, Dock.Right);
        footer.Children.Add(remove);
        if (!wall)
        {
            var all = PaperButton(0xE8A7, "Open Notes", () => ShowPage(DashboardPage.Notes));
            DockPanel.SetDock(all, Dock.Right);
            footer.Children.Add(all);
        }
        footer.Children.Add(new Border());
        DockPanel.SetDock(footer, Dock.Bottom);
        layout.Children.Add(footer);
        layout.Children.Add(text);
        paper.Child = layout;
        text.TextChanged += (_, _) => save(false);
        text.LostKeyboardFocus += (_, _) => save(true);
        paper.Unloaded += (_, _) => save(true);
        if (focusNote == note.Id)
        {
            focusNote = null;
            paper.Loaded += (_, _) => text.Focus();
        }
        return paper;

        void Apply(Func<DockNote, DockNote?> change)
        {
            try
            {
                DockNotes.Change(note.Id, change);
                ShowPage(currentPage);
            }
            catch (Exception error) when (DockActions.IsActionError(error)) { ShowOwnedMessage(error.Message); }
        }
    }

    private Button PaperButton(int icon, string label, Action action)
    {
        var button = IconButton(icon, label, action);
        button.Width = 26;
        button.Height = 24;
        button.Foreground = paperInk;
        ((TextBlock)button.Content).FontSize = 11;
        button.Resources["ChipHover"] = Brush("#22000000");
        button.Resources["ControlFocus"] = paperInk;
        return button;
    }

    // Returns a function to call on every edit. It writes a moment after typing stops; pass true to write right now
    // (losing focus, leaving the page) and false to schedule.
    private Action<bool> SaveSoon(Guid id, Func<string> read)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        var dirty = false;
        var removed = false;
        timer.Tick += (_, _) => Write();
        return now =>
        {
            if (removed)
                return;
            if (!now)
            {
                dirty = true;
                timer.Stop();
                timer.Start();
                return;
            }
            Write();
        };

        void Write()
        {
            timer.Stop();
            if (!dirty)
                return;
            dirty = false;
            var text = read();
            try { DockNotes.Change(id, saved => saved with { Text = text, Updated = DateTimeOffset.UtcNow }); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // Keep it marked so the next edit or focus change tries again; the text is still on screen.
                dirty = true;
                removed = closed;
            }
        }
    }

    private FrameworkElement MemoDesk()
    {
        var memos = DockNotes.Load().Where(note => note.Kind == DockNotes.Memo).OrderByDescending(note => note.Updated).ToArray();
        if (memos.Length == 0)
        {
            var empty = Text("No memos yet. A memo is for anything too long for a sticky.", 13, AssistantFaint);
            empty.Margin = new Thickness(0, 6, 0, 0);
            return empty;
        }
        var current = memos.FirstOrDefault(note => note.Id == openMemo) ?? memos[0];
        openMemo = current.Id;

        var list = new StackPanel { Margin = new Thickness(-8, 0, 0, 0) };
        var titles = new Dictionary<Guid, TextBlock>();
        foreach (var memo in memos)
        {
            var title = Text(DockNotes.Title(memo), 13);
            titles[memo.Id] = title;
            var when = Text(memo.Updated.LocalDateTime.Date == DateTime.Today ? memo.Updated.LocalDateTime.ToString("t")
                : memo.Updated.LocalDateTime.ToString("MMM d"), 11, AssistantFaint);
            var row = PlainButton(new StackPanel { Children = { title, when } }, "Open memo: " + DockNotes.Title(memo), () =>
            {
                openMemo = memo.Id;
                ShowPage(DashboardPage.Notes);
            });
            row.Margin = new Thickness(0, 0, 0, 1);
            row.Padding = new Thickness(8, 6, 8, 6);
            row.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            if (memo.Id == current.Id)
                row.Background = controlSelected;
            list.Children.Add(row);
        }

        var editor = Field("Memo", DockNotes.TextLimit);
        editor.Text = current.Text;
        editor.AcceptsReturn = true;
        editor.AcceptsTab = true;
        editor.TextWrapping = TextWrapping.Wrap;
        editor.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        editor.Tag = "The first line becomes the title.";
        editor.FontSize = 13;
        editor.Padding = new Thickness(12, 10, 12, 10);
        editor.Height = 262;
        var save = SaveSoon(current.Id, () => editor.Text);
        editor.TextChanged += (_, _) =>
        {
            save(false);
            titles[current.Id].Text = DockNotes.Title(current with { Text = editor.Text });
        };
        editor.LostKeyboardFocus += (_, _) => save(true);
        editor.Unloaded += (_, _) => save(true);
        if (focusNote == current.Id)
        {
            focusNote = null;
            editor.Loaded += (_, _) => editor.Focus();
        }

        var footer = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        var remove = QuietButton("Delete this memo", async () =>
        {
            var wasModal = modalOpen;
            modalOpen = true;
            try
            {
                if (editor.Text.Trim().Length > 0 && !await ConfirmAsync(this, "Delete this memo? It cannot be brought back.", "Delete"))
                    return;
            }
            finally { modalOpen = wasModal; }
            try
            {
                DockNotes.Change(current.Id, _ => null);
                openMemo = null;
                ShowPage(DashboardPage.Notes);
            }
            catch (Exception error) when (DockActions.IsActionError(error)) { ShowOwnedMessage(error.Message); }
        });
        DockPanel.SetDock(remove, Dock.Right);
        footer.Children.Add(remove);
        var copy = QuietButton("Copy all", () =>
        {
            try { Clipboard.SetText(editor.Text); }
            catch (System.Runtime.InteropServices.ExternalException) { }
        });
        copy.HorizontalAlignment = HorizontalAlignment.Left;
        footer.Children.Add(copy);

        var writing = new StackPanel { Children = { editor, footer } };
        var scroller = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 300 };
        return Panes((.42, scroller), (1, writing));
    }

    // The sticky on Today: the pinned one, or a blank that becomes a real note with the first keystroke.
    private FrameworkElement TodaySticky()
    {
        var note = DockNotes.ForToday(DockNotes.Load());
        if (note != null)
            return StickyCard(note, double.NaN, 96, wall: false);

        var paper = new Border
        {
            Background = Brush(DockNotes.PaperHex(null)), CornerRadius = new CornerRadius(3), Height = 96,
            Padding = new Thickness(10, 8, 8, 6)
        };
        var text = new TextBox
        {
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Foreground = paperInk, CaretBrush = paperInk, FontSize = 13, MaxLength = DockNotes.TextLimit, Padding = new Thickness(0)
        };
        AutomationProperties.SetName(text, "Sticky note");
        var hint = new TextBlock { Text = "Jot something down. It stays here.", Foreground = paperSoftInk, FontSize = 13, IsHitTestVisible = false };
        Guid? created = null;
        Action<bool>? save = null;
        text.TextChanged += (_, _) =>
        {
            hint.Visibility = text.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (created == null)
            {
                if (text.Text.Trim().Length == 0)
                    return;
                try { created = DockNotes.Add(DockNotes.Sticky, text.Text).Id; }
                catch (Exception error) when (DockActions.IsActionError(error)) { return; }
                save = SaveSoon(created.Value, () => text.Text);
            }
            save?.Invoke(false);
        };
        text.LostKeyboardFocus += (_, _) => save?.Invoke(true);
        paper.Unloaded += (_, _) => save?.Invoke(true);
        paper.Child = new Grid { Children = { hint, text } };
        return paper;
    }
}
