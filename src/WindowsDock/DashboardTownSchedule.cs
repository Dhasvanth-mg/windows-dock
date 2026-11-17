using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace WindowsDock;

public partial class DashboardWindow
{
    private void ShowTownSchedule(Guid slot)
    {
        if (DockLife.Load().FirstOrDefault(item => item.Id == slot) is not { } saved)
            return;
        var world = DockLife.Tick(saved, DateTimeOffset.UtcNow);
        var (window, content, _) = CreateDockPanel("Town schedule", world.World, 730, 0xE787, accent,
            backAction: () => ShowLifeSimulator(slot), backLabel: "Back to life simulator", height: 620, expandable: true);
        var areas = DockLife.Venues(world.World);
        var date = Text("", 17, AssistantInk, true);
        var venues = new StackPanel();
        var residentDetail = Text("", 12, AssistantSecondary);
        residentDetail.TextWrapping = TextWrapping.Wrap;
        var people = world.People.Where(person => person.Alive).OrderBy(person => person.Name).ToArray();
        var roster = new ComboBox { DisplayMemberPath = nameof(LifePerson.Name), ItemsSource = people,
            SelectedItem = people.FirstOrDefault() };
        AutomationProperties.SetName(roster, "Resident schedule");
        var offset = 0;
        var dayPicker = new WrapPanel();
        foreach (var (label, increment) in new[] { ("Previous day", -1), ("Today", 0), ("Next day", 1) })
        {
            var button = ActionButton(label, () =>
            {
                offset = increment == 0 ? 0 : Math.Clamp(offset + increment, -7, 14);
                Render();
            });
            button.Margin = new Thickness(0, 0, 5, 5);
            dayPicker.Children.Add(button);
        }
        content.Children.Add(date);
        content.Children.Add(dayPicker);
        content.Children.Add(Labeled("Resident", roster));
        content.Children.Add(residentDetail);
        content.Children.Add(SectionTitle("Businesses and venues"));
        content.Children.Add(venues);
        roster.SelectionChanged += (_, _) => RenderResident();
        Render();
        window.Show();

        DateTimeOffset CurrentDay() => world.At.AddDays(offset);

        void Render()
        {
            var at = CurrentDay();
            date.Text = at.ToString("dddd, MMM d yyyy");
            venues.Children.Clear();
            foreach (var place in areas)
            {
                var state = offset != 0 ? place.Hours(at.DayOfWeek) :
                    $"{(place.IsOpen(at) ? "Open now" : "Closed now")} · {place.Hours(at.DayOfWeek)}";
                var line = Card(new StackPanel { Children = { Text(place.Name, 13, AssistantInk, true),
                    Text($"{place.District} · {state}", 12, AssistantSecondary) } });
                line.Margin = new Thickness(0, 0, 0, 4);
                venues.Children.Add(line);
            }
            var opening = DockLife.NextGathering(world.World, new DateTimeOffset(at.UtcDateTime.Date, TimeSpan.Zero));
            if (opening.Date == DateOnly.FromDateTime(at.UtcDateTime))
                venues.Children.Add(Text($"{opening.Title} · 18:00 to 20:00 · {opening.Venue.Name}", 12, AssistantInk));
            RenderResident();
        }

        void RenderResident()
        {
            if (roster.SelectedItem is not LifePerson person)
            {
                residentDetail.Text = "There are no living residents to show.";
                return;
            }
            var at = CurrentDay();
            var day = DateOnly.FromDateTime(at.UtcDateTime);
            var block = DockLife.Duty(person, day, areas);
            var lines = new List<string>();
            if (offset == 0)
            {
                var present = DockLife.Presence(world, person);
                lines.Add($"Now · {present.Activity} · {present.Place}");
            }
            if (block != null)
                lines.Add($"{block.Starts:00}:00 to {block.Ends:00}:00 · {block.Venue.Name} · {block.Venue.District}");
            else
                lines.Add($"Day off · Home in {person.Location}");
            var evening = new DateTimeOffset(at.UtcDateTime.Date.AddHours(LifeGathering.Starts), TimeSpan.Zero);
            var presentAtSix = DockLife.Presence(world with { At = evening }, person);
            if (at.DayOfWeek == DayOfWeek.Saturday && presentAtSix.Activity is not ("At home" or "At work" or "School" or "Deceased"))
                lines.Add($"18:00 to 20:00 · {presentAtSix.Activity} · {presentAtSix.Place}");
            residentDetail.Text = string.Join("\n", lines);
        }
    }
}
