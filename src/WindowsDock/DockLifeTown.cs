namespace WindowsDock;

internal enum LifeVenueKind { School, Market, Clinic, Workshop, Library, Hall }
internal sealed record LifeVenue(LifeVenueKind Kind, string Name, string District, int Opens, int Closes, DayOfWeek[] Closed)
{
    internal bool IsOpen(DateTimeOffset at) => !Closed.Contains(at.DayOfWeek) && at.Hour >= Opens && at.Hour < Closes;
    internal string Hours(DayOfWeek day) => Closed.Contains(day) ? "Closed" : $"{Opens:00}:00 to {Closes:00}:00";
}

internal sealed record LifePresence(string District, string Place, string Activity);
internal sealed record LifeBlock(int Starts, int Ends, LifeVenue Venue, string Activity);
internal sealed record LifeGathering(DateOnly Date, string Title, LifeVenue Venue)
{
    internal const int Starts = 18;
    internal const int Ends = 20;
}

internal sealed record LifeTownBond(Guid First, Guid Second, int Meetings, DateOnly LastMet);

internal static partial class DockLife
{
    private static readonly DayOfWeek[] Weekend = [DayOfWeek.Saturday, DayOfWeek.Sunday];
    private static readonly DayOfWeek[] Sundays = [DayOfWeek.Sunday];

    internal static LifeVenue[] Venues(string world)
    {
        var districts = Places(world);
        var names = world.Contains("cyber", StringComparison.OrdinalIgnoreCase)
            ? new[] { "Grid Academy", "Night Market stalls", "Medtech clinic", "Fabrication shop", "City archive", "Signal Hall" }
            : world.Contains("medieval", StringComparison.OrdinalIgnoreCase)
                ? ["Village school", "Market stalls", "Herbalist's shop", "Forge", "Scriptorium", "Guild Hall"]
                : ["Public school", "Market hall", "Clinic", "Workshop", "Library", "Community hall"];
        return
        [
            new(LifeVenueKind.School, names[0], districts[0], 8, 17, Weekend),
            new(LifeVenueKind.Market, names[1], districts[2], 8, 19, Sundays),
            new(LifeVenueKind.Clinic, names[2], districts[1], 8, 21, []),
            new(LifeVenueKind.Workshop, names[3], districts[1], 8, 18, Weekend),
            new(LifeVenueKind.Library, names[4], districts[0], 9, 19, Sundays),
            new(LifeVenueKind.Hall, names[5], districts[0], 10, 22, [])
        ];
    }

    private static LifeVenue Workplace(LifeVenue[] venues, LifePerson person) => venues[(int)(person.Job switch
    {
        "Teacher" or "Scribe" or "Student" => LifeVenueKind.School,
        "Nurse" or "Herbalist" or "Medtech" => LifeVenueKind.Clinic,
        "Shopkeeper" or "Baker" or "Courier" => LifeVenueKind.Market,
        "Archivist" => LifeVenueKind.Library,
        _ => LifeVenueKind.Workshop
    })];

    internal static LifeBlock? Duty(LifePerson person, DateOnly date, LifeVenue[] venues)
    {
        var age = Age(person, date);
        if (!person.Alive || age < 5 || person.Job == "Retired" || age >= 65 || age is < 18 && person.Job != "Student")
            return null;
        var workplace = Workplace(venues, person);
        if (workplace.Closed.Contains(date.DayOfWeek))
            return null;
        if (person.Job == "Student")
            return new(8, 15, workplace, "School");
        var rotation = (int)(uint)person.Id.GetHashCode() & 0x7fffffff;
        if (Weekend.Contains(date.DayOfWeek) && rotation % 2 != date.DayNumber % 2)
            return null;
        var start = Math.Max(workplace.Opens, 8 + rotation % 3);
        return new(start, Math.Min(start + 8, workplace.Closes), workplace, "At work");
    }

    internal static LifeGathering NextGathering(string world, DateTimeOffset from)
    {
        var days = ((int)DayOfWeek.Saturday - (int)from.DayOfWeek + 7) % 7;
        if (days == 0 && from.Hour >= LifeGathering.Ends)
            days = 7;
        return Gathering(world, DateOnly.FromDateTime(from.UtcDateTime).AddDays(days), Venues(world));
    }

    private static LifeGathering Gathering(string world, DateOnly date, LifeVenue[] venues)
    {
        var title = (date.DayNumber / 7 % 3) switch
        {
            0 => "Community supper",
            1 when world.Contains("medieval", StringComparison.OrdinalIgnoreCase) => "Guild market",
            1 => "Makers' meetup",
            _ => "Games night"
        };
        return new(date, title, venues[(int)LifeVenueKind.Hall]);
    }

    private static LifePerson[] Attendees(int seed, DateOnly date, IEnumerable<LifePerson> people) => people
        .Where(person => person.Alive && Age(person, date) >= 8)
        .OrderBy(person => MeetingOrder(seed, date, person))
        .ThenBy(person => person.Id)
        .Take(6).ToArray();

    private static int MeetingOrder(int seed, DateOnly date, LifePerson person) =>
        new Random(unchecked(seed ^ (date.DayNumber * 115547) ^ person.Id.GetHashCode())).Next();

    internal static LifePresence Presence(LifeSave world, LifePerson person)
    {
        if (!person.Alive)
            return new(person.Location, person.Location, "Deceased");
        var date = DateOnly.FromDateTime(world.At.UtcDateTime);
        var venues = Venues(world.World);
        if (Duty(person, date, venues) is { } shift && world.At.Hour >= shift.Starts && world.At.Hour < shift.Ends)
            return new(shift.Venue.District, shift.Venue.Name, shift.Activity);
        if (world.At.DayOfWeek == DayOfWeek.Saturday && world.At.Hour >= LifeGathering.Starts && world.At.Hour < LifeGathering.Ends &&
            Attendees(world.Seed, date, world.People).Any(resident => resident.Id == person.Id))
        {
            var gathering = Gathering(world.World, date, venues);
            return new(gathering.Venue.District, gathering.Venue.Name, gathering.Title);
        }
        return new(person.Location, person.Location, "At home");
    }

    internal static bool CanAttend(LifeSave world)
    {
        var gathering = NextGathering(world.World, world.At);
        var date = DateOnly.FromDateTime(world.At.UtcDateTime);
        return date == gathering.Date && world.At.Hour >= LifeGathering.Starts && world.At.Hour < LifeGathering.Ends &&
            world.Location == gathering.Venue.District && world.LastGathering != date;
    }

    private static LifeSave Attend(LifeSave world)
    {
        var gathering = NextGathering(world.World, world.At);
        if (!CanAttend(world))
            throw new ArgumentException($"The next gathering is {gathering.Date:ddd, MMM d} at 18:00 in {gathering.Venue.District}. You can attend once.");
        var today = gathering.Date;
        var guests = Attendees(world.Seed, today, world.People).Select(person => person.Id).ToHashSet();
        world = Advance(world, world.At.AddHours(1));
        var entry = new LifeEvent(world.At, $"{world.Player} joined {gathering.Title.ToLowerInvariant()} at {gathering.Venue.Name}.");
        return world with
        {
            LastGathering = today, History = [.. world.History.TakeLast(279), entry],
            People = world.People.Select(person => guests.Contains(person.Id)
                ? Remember(person with { Affinity = (int)Math.Clamp((long)person.Affinity + 1, 0, 100) }, entry)
                : person).ToArray()
        };
    }
}
