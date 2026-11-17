using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace WindowsDock;

public partial class DashboardWindow
{
    private void ShowLifeSimulator() => ShowLifeSimulator(null);

    private void ShowLifeSimulator(Guid? initial)
    {
        var (window, content, cancellation) = CreateDockPanel("Life simulator", "A town that keeps its own time", 800, 0xE80F, accent,
            height: 640, expandable: true);
        var saves = new ComboBox { DisplayMemberPath = nameof(LifeSlot.Label) };
        AutomationProperties.SetName(saves, "Life save slot");
        var playerName = Field("Player name", 100);
        var worldName = Field("World name", 100);
        playerName.Tag = "Your name";
        worldName.Tag = "Town name or setting";
        var creation = new Expander { Header = "New life", Style = (Style)window.FindResource("AppletSection") };
        var status = Text("", 12, AssistantSecondary);
        status.TextWrapping = TextWrapping.Wrap;
        creation.Content = new StackPanel { Children =
        {
            Labeled("Character", playerName), Labeled("World", worldName),
            Text("Use a town name or include “cyberpunk” or “medieval” for a different setting.", 11, AssistantSecondary),
            ActionButton("Create life", () => Run(() =>
            {
                var created = DockLife.Create(playerName.Text, worldName.Text, DateTimeOffset.UtcNow);
                DockLife.SaveNew(created);
                Reload(created.Id);
            }))
        } };
        var stage = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        content.Children.Add(Labeled("Saved lives", saves));
        content.Children.Add(creation);
        content.Children.Add(status);
        content.Children.Add(stage);
        LifeSave? live = null;
        var changedAt = DateTimeOffset.MinValue;
        var clock = Text("", 20, AssistantInk, strong: true);
        var stats = Text("", 12, AssistantSecondary);
        stats.TextWrapping = TextWrapping.Wrap;
        var diary = new StackPanel();
        var profile = Text("", 13, AssistantInk);
        profile.TextWrapping = TextWrapping.Wrap;
        var portrait = new Image { Width = 48, Height = 48, Margin = new Thickness(0, 0, 9, 0) };
        Guid? illustrated = null;
        var decisions = new WrapPanel();
        var memories = new StackPanel();
        var people = new ComboBox { DisplayMemberPath = nameof(LifePersonItem.Label) };
        AutomationProperties.SetName(people, "People in this world");
        var places = new ComboBox();
        AutomationProperties.SetName(places, "Life simulator location");
        var careers = new ComboBox();
        AutomationProperties.SetName(careers, "Career to apply for");
        var apply = ActionButton("Apply for job");
        var action = Field("Action to take", 120);
        action.Tag = "Type an action, such as “help” or “share meal”";
        var timer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher) { Interval = TimeSpan.FromSeconds(1) };
        action.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter)
            {
                SubmitTextAction();
                args.Handled = true;
            }
        };
        apply.Click += (_, _) => TakeAction("apply " + careers.SelectedItem);
        timer.Tick += (_, _) =>
        {
            if (live is not { Speed: > 0 } current || cancellation.IsCancellationRequested)
                return;
            var now = DateTimeOffset.UtcNow;
            var preview = DockLife.Tick(current, now);
            if (now >= changedAt.AddSeconds(15) || preview.History.LastOrDefault() != current.History.LastOrDefault())
                Commit(item => item);
            else
                UpdateClock(preview);
        };
        window.IsVisibleChanged += (_, _) => SetTimer();
        window.Closed += (_, _) => timer.Stop();
        saves.SelectionChanged += (_, _) =>
        {
            if (saves.SelectedItem is not LifeSlot slot)
                return;
            Run(() =>
            {
                live = DockLife.Change(slot.Id, item => DockLife.Tick(item, DateTimeOffset.UtcNow));
                changedAt = DateTimeOffset.UtcNow;
                Mount();
                SetTimer();
            });
        };
        people.SelectionChanged += (_, _) => RenderProfile();
        Reload(initial);
        window.Show();

        void Reload(Guid? id)
        {
            var slots = DockLife.Load().Select(item => new LifeSlot(item.Id, $"{item.Player} · {item.World}")).ToArray();
            saves.ItemsSource = slots;
            saves.SelectedItem = slots.FirstOrDefault(item => item.Id == id) ?? slots.FirstOrDefault();
            if (slots.Length == 0)
                creation.IsExpanded = true;
            else
                creation.IsExpanded = false;
        }

        bool Run(Action work)
        {
            try { work(); status.Text = ""; return true; }
            catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
            { status.Text = error.Message; return false; }
        }

        bool Commit(Func<LifeSave, LifeSave> edit)
        {
            if (live is not { } current)
                return false;
            return Run(() =>
            {
                var now = DateTimeOffset.UtcNow;
                live = DockLife.Change(current.Id, item => edit(DockLife.Tick(item, now)));
                changedAt = now;
                Render();
                SetTimer();
            });
        }

        bool TakeAction(string text)
        {
            var person = (people.SelectedItem as LifePersonItem)?.Id;
            return Commit(item => DockLife.Act(item, text, person));
        }

        void SubmitTextAction()
        {
            if (TakeAction(action.Text))
                action.Clear();
        }

        void SetTimer()
        {
            if (window.IsVisible && live is { Speed: > 0 } && !cancellation.IsCancellationRequested)
                timer.Start();
            else
                timer.Stop();
        }

        void Mount()
        {
            var world = live!;
            stage.Children.Clear();
            foreach (var element in new FrameworkElement[] { clock, stats, action, places, people, profile, portrait,
                decisions, memories, diary, careers, apply })
                if (element.Parent is Panel previous)
                    previous.Children.Remove(element);
            var summary = Card(new StackPanel { Children = { clock, stats } });
            stage.Children.Add(summary);
            var speeds = new WrapPanel();
            foreach (var rate in new[] { 0, 1, 2, 5, 10, 25, 50, 100 })
                speeds.Children.Add(QuietButton(rate == 0 ? "Pause" : $"{rate}×", () =>
                    Commit(item => DockLife.SetSpeed(item, rate, DateTimeOffset.UtcNow))));
            var custom = Field("Custom clock speed", 7);
            custom.Width = 76;
            custom.Text = world.Speed.ToString(CultureInfo.CurrentCulture);
            speeds.Children.Add(custom);
            speeds.Children.Add(QuietButton("Set speed", () => Commit(item => DockLife.SetSpeed(item,
                double.TryParse(custom.Text, out var speed) ? speed : throw new ArgumentException("Enter a speed from 0 to 1,000×."),
                DateTimeOffset.UtcNow))));
            stage.Children.Add(speeds);
            stage.Children.Add(new WrapPanel { Children =
            {
                QuietButton("Skip hour", () => Commit(item => DockLife.Advance(item, item.At.AddHours(1)))),
                QuietButton("Skip day", () => Commit(item => DockLife.Advance(item, item.At.AddDays(1)))),
                QuietButton("Skip month", () => Commit(item => DockLife.Advance(item, item.At.AddMonths(1)))),
                QuietButton("Skip year", () => Commit(item => DockLife.Advance(item, item.At.AddYears(1))))
            } });
            var leap = Field("Amount of time to skip", 4);
            leap.Width = 64;
            leap.Text = "1";
            var unit = new ComboBox { ItemsSource = new[] { "Hours", "Days", "Months", "Years" }, SelectedIndex = 1 };
            AutomationProperties.SetName(unit, "Time skip unit");
            stage.Children.Add(new WrapPanel { Children = { leap, unit, QuietButton("Skip custom time", () => Commit(item =>
            {
                if (!int.TryParse(leap.Text, out var amount) || amount < 1 || amount > 3650 ||
                    (unit.SelectedIndex == 3 && amount > 100) || (unit.SelectedIndex == 2 && amount > 1200))
                    throw new ArgumentException("Enter up to 3,650 hours or days, 1,200 months, or 100 years.");
                var future = unit.SelectedIndex switch
                {
                    0 => item.At.AddHours(amount), 1 => item.At.AddDays(amount), 2 => item.At.AddMonths(amount), _ => item.At.AddYears(amount)
                };
                return DockLife.Advance(item, future);
            })) } });

            var actions = new WrapPanel();
            foreach (var verb in new[] { "Work", "Study", "Rest", "Buy groceries" })
                actions.Children.Add(ActionButton(verb, () => TakeAction(verb)));
            places.ItemsSource = DockLife.Places(world.World);
            places.SelectedItem = world.Location;
            careers.ItemsSource = DockLife.Jobs(world.World);
            careers.SelectedItem = string.IsNullOrEmpty(world.Career) ? careers.Items[0] : world.Career;
            stage.Children.Add(Card(new StackPanel { Children =
            {
                SectionTitle("What do you do?"), actions, action, QuietButton("Take action", SubmitTextAction),
                new WrapPanel { Children = { places, QuietButton("Travel", () => TakeAction("travel " + places.SelectedItem)) } },
                new WrapPanel { Children = { careers, apply } }
            } }));
            var personal = new DockPanel { Margin = new Thickness(0, 4, 0, 4), Children = { portrait, profile } };
            stage.Children.Add(Card(new StackPanel { Children =
            {
                SectionTitle("People"), people, personal, decisions, memories,
                new WrapPanel { Children =
                {
                    QuietButton("My relationships", () => OpenGraph(live?.Id)),
                    QuietButton("Selected person's relationships", () => OpenGraph((people.SelectedItem as LifePersonItem)?.Id))
                } }
            } }));
            stage.Children.Add(Card(new StackPanel { Children = { SectionTitle("Timeline"), diary } }));
            Render();
        }

        void OpenGraph(Guid? focus)
        {
            if (live is { } world)
                ShowLifeRelationships(world.Id, focus ?? world.Id);
        }

        void UpdateClock(LifeSave world)
        {
            clock.Text = $"{world.At:ddd, MMM d yyyy · HH:mm}   {(world.Speed == 0 ? "Paused" : $"{world.Speed:0.##}×")}";
            stats.Text = $"{world.Location} · {world.Cash:N2} cash · {world.Groceries} groceries · " +
                $"{world.Work} shifts · study level {world.Study} · {world.People.Count(person => person.Alive)} residents" +
                (string.IsNullOrEmpty(world.Career) ? "" : $" · {world.Career}");
        }

        void Render()
        {
            if (live is not { } world)
                return;
            UpdateClock(world);
            apply.IsEnabled = world.Study >= 2;
            apply.ToolTip = world.Study >= 2 ? "An application takes 3 simulated hours" : "Study twice to apply";
            var selected = (people.SelectedItem as LifePersonItem)?.Id;
            var entries = world.People.OrderByDescending(person => person.Alive).ThenBy(person => person.Name)
                .Select(person => new LifePersonItem(person.Id, $"{person.Name} · {(person.Alive ? person.Job : "Deceased")}")).ToArray();
            if (people.ItemsSource is not LifePersonItem[] lastPeople || !lastPeople.SequenceEqual(entries))
            {
                people.ItemsSource = entries;
                people.SelectedItem = entries.FirstOrDefault(person => person.Id == selected) ?? entries.FirstOrDefault();
            }
            RenderProfile();
            diary.Children.Clear();
            foreach (var entry in world.History.Reverse().Take(36))
            {
                var row = Text($"{entry.At:MMM d yyyy · HH:mm}   {entry.Text}", 12, AssistantInk);
                row.TextWrapping = TextWrapping.Wrap;
                row.Margin = new Thickness(0, 3, 0, 3);
                diary.Children.Add(row);
            }
        }

        void RenderProfile()
        {
            decisions.Children.Clear();
            memories.Children.Clear();
            if (live is not { } world || people.SelectedItem is not LifePersonItem row ||
                world.People.FirstOrDefault(person => person.Id == row.Id) is not { } person)
            {
                profile.Text = "";
                portrait.Source = null;
                illustrated = null;
                return;
            }
            if (illustrated != person.Id)
            {
                portrait.Source = LifePortrait(person.Id);
                illustrated = person.Id;
            }
            portrait.Opacity = person.Alive ? 1 : .55;
            AutomationProperties.SetName(portrait, $"Portrait of {person.Name}");
            var partner = world.Partner == person.Id ? world.Player : world.People.FirstOrDefault(item => item.Id == person.Partner)?.Name;
            var age = person.Alive ? DockLife.Age(person, DateOnly.FromDateTime(world.At.UtcDateTime)) : person.Lifespan;
            profile.Text = $"{person.Name} · {age} · {person.Trait} · {person.Location}\n" +
                $"{person.Job} · {person.Worked} shifts · {person.Savings:N2} savings" +
                (partner == null ? "" : $" · {(person.Married || (world.Partner == person.Id && world.Married) ? "spouse" : "partner")} {partner}") +
                (person.Children == 0 ? "" : $" · {person.Children} children") + $" · rapport {person.Affinity}";
            if (person.Alive && person.Location != world.Location)
                decisions.Children.Add(QuietButton($"Visit {person.Location}", () => TakeAction("travel " + person.Location)));
            else
                foreach (var choice in DockLife.SocialChoices(world, person))
                {
                    var button = ActionButton(choice.Label, () => TakeAction(choice.Command));
                    button.Margin = new Thickness(0, 3, 5, 3);
                    button.ToolTip = $"{choice.Hours}h" + (choice.Cost > 0 ? $" · costs {choice.Cost:N2}" : "");
                    decisions.Children.Add(button);
                }
            foreach (var memory in (person.Memories ?? []).TakeLast(3).Reverse())
            {
                var note = Text($"{memory.At:MMM d} · {memory.Text}", 11, AssistantSecondary);
                note.TextWrapping = TextWrapping.Wrap;
                memories.Children.Add(note);
            }
        }
    }

    private sealed record LifeSlot(Guid Id, string Label);
    private sealed record LifePersonItem(Guid Id, string Label);
}
