using System.IO;
using System.Security.Cryptography;

namespace WindowsDock;

internal sealed record LifePerson(Guid Id, string Name, string Trait, DateOnly Birth, int Lifespan, string Job, string Location,
    decimal Savings, int Worked, bool Alive = true, Guid? Partner = null, Guid? Parent = null, int Children = 0, int Affinity = 0,
    Guid? OtherParent = null, bool Married = false, int Dates = 0, LifeEvent[]? Memories = null);
internal sealed record LifeEvent(DateTimeOffset At, string Text);
internal sealed record LifeSave(Guid Id, string Player, string World, int Seed, DateTimeOffset At, DateTimeOffset ClockAnchor, double Speed,
    LifePerson[] People, LifeEvent[] History, decimal Cash, int Work, int Study, int Groceries, string Location, decimal Prices,
    Guid? Partner = null, bool Married = false, string Career = "");

internal static partial class DockLife
{
    private const string Store = "life-simulator.json";
    private static readonly DateTimeOffset LastDate = new(2199, 12, 31, 23, 59, 0, TimeSpan.Zero);
    private static readonly string[] Names = ["Avery", "Noor", "Sam", "Quinn", "Jordan", "Remy", "Cameron", "Robin", "Toni", "Kit",
        "Rowan", "Jamie", "Emery", "Parker", "Alex", "Eden", "Taylor", "Sky", "River", "Sage"];
    private static readonly string[] Traits = ["quiet", "curious", "outgoing", "methodical", "restless", "patient"];
    internal static LifeSave[] Load() => DockData.Read<LifeSave[]>(Store) ?? [];

    internal static LifeSave Create(string player, string world, DateTimeOffset now, int? fixedSeed = null)
    {
        player = DockDeskspaces.Label(player);
        world = DockDeskspaces.Label(world);
        var seed = fixedSeed ?? RandomNumberGenerator.GetInt32(int.MaxValue);
        var random = new Random(seed);
        var begin = new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);
        var places = Places(world);
        var jobs = Jobs(world);
        var people = Enumerable.Range(0, 10).Select(index => NewPerson(random, Names[index],
            DateOnly.FromDateTime(begin.UtcDateTime).AddYears(-random.Next(18, 65)).AddDays(-random.Next(180)),
            jobs[index % jobs.Length], places[index % places.Length])).ToArray();
        return new(Guid.NewGuid(), player, world, seed, begin, now, 0, people,
            [new(begin, $"{player} arrived in {world}. The town is already at work.")], 250, 0, 0, 0, places[0], 1);
    }

    internal static void SaveNew(LifeSave world) => DockData.Update<LifeSave[]>(Store, saves => [world, .. saves ?? []]);

    internal static LifeSave Change(Guid id, Func<LifeSave, LifeSave> update)
    {
        LifeSave? result = null;
        DockData.Update<LifeSave[]>(Store, saves => (saves ?? []).Select(item => item.Id == id ? result = update(item) : item).ToArray());
        return result ?? throw new IOException("This life simulator slot is missing.");
    }

    internal static LifeSave Tick(LifeSave world, DateTimeOffset realNow)
    {
        if (realNow <= world.ClockAnchor)
            return world;
        var elapsed = (realNow - world.ClockAnchor).TotalSeconds * world.Speed;
        var limit = (LastDate - world.At).TotalSeconds;
        return elapsed <= 0 ? world with { ClockAnchor = realNow } :
            Advance(world, world.At.AddSeconds(Math.Min(limit, elapsed))) with { ClockAnchor = realNow };
    }

    internal static LifeSave SetSpeed(LifeSave world, double speed, DateTimeOffset now)
    {
        if (speed < 0 || speed > 1000 || !double.IsFinite(speed))
            throw new ArgumentException("Choose a speed from 0 to 1,000×.");
        return Tick(world, now) with { Speed = speed };
    }

    internal static LifeSave Advance(LifeSave world, DateTimeOffset target)
    {
        target = target > LastDate ? LastDate : target;
        if (target <= world.At)
            return world;
        var people = world.People.ToList();
        var history = new Queue<LifeEvent>(world.History);
        var places = Places(world.World);
        var jobs = Jobs(world.World);
        var prices = world.Prices;
        for (var day = world.At.UtcDateTime.Date; day <= target.UtcDateTime.Date; day = day.AddDays(1))
        {
            var date = DateOnly.FromDateTime(day);
            var morning = new DateTimeOffset(day.AddHours(8), TimeSpan.Zero);
            var evening = morning.AddHours(10);
            var random = new Random(unchecked(world.Seed ^ (date.DayNumber * 16777619)));
            if (morning > world.At && morning <= target)
            {
                if (date.Day == 1)
                {
                    prices = Math.Clamp(Math.Round(prices * (decimal)(0.96 + random.NextDouble() * .09), 3), .65m, 2.5m);
                    Event(morning, $"Market prices shifted to {prices:P0} of the starting prices.");
                    if (people.Count(item => item.Alive) < 8)
                    {
                        var newcomer = NewPerson(random, FreshName(), date.AddYears(-random.Next(20, 47)), jobs[random.Next(jobs.Length)], places[0]);
                        people.Add(newcomer);
                        Event(morning, $"{newcomer.Name} moved to {world.World}.");
                    }
                }
                for (var index = 0; index < people.Count; index++)
                {
                    var person = people[index];
                    if (!person.Alive)
                        continue;
                    var age = Age(person, date);
                    if (age >= person.Lifespan)
                    {
                        people[index] = person with { Alive = false, Job = "Deceased" };
                        PersonEvent(morning, $"{person.Name} died at age {age}.", index);
                    }
                    else if (age >= 65 && person.Job != "Retired")
                    {
                        people[index] = person with { Job = "Retired" };
                        PersonEvent(morning, $"{person.Name} retired.", index);
                    }
                    else if (age >= 18 && person.Job == "Student")
                    {
                        people[index] = person with { Job = jobs[random.Next(jobs.Length)] };
                        PersonEvent(morning, $"{person.Name} graduated and started work as {people[index].Job.ToLowerInvariant()}.", index);
                    }
                    else if (age is >= 18 and < 65 && day.DayOfWeek is not (DayOfWeek.Sunday or DayOfWeek.Saturday))
                    {
                        var job = random.Next(440) == 0 ? jobs[random.Next(jobs.Length)] : person.Job;
                        people[index] = person with { Job = job, Savings = person.Savings + 22, Worked = person.Worked + 1 };
                        if (job != person.Job)
                            PersonEvent(morning, $"{person.Name} changed careers to {job.ToLowerInvariant()}.", index);
                    }
                }
            }
            // A separate seed keeps each scheduled phase reproducible when the clock crosses it in a different call.
            random = new Random(unchecked((world.Seed + 827) ^ (date.DayNumber * 16777619)));
            if (evening <= world.At || evening > target)
                continue;
            var adults = people.Where(item => item.Alive && Age(item, date) >= 18).ToArray();
            if (adults.Length == 0)
                continue;
            var chosen = adults[random.Next(adults.Length)];
            var first = people.FindIndex(item => item.Id == chosen.Id);
            if (random.Next(25) == 0)
            {
                var place = places[random.Next(places.Length)];
                if (place != chosen.Location)
                {
                    people[first] = chosen = chosen with { Location = place };
                    PersonEvent(evening, $"{chosen.Name} moved to {place}.", first);
                    chosen = people[first];
                }
            }
            if (chosen.Id == world.Partner)
                continue;
            var unpaired = adults.Where(person => person.Id != chosen.Id && person.Id != world.Partner &&
                !Related(chosen, person) && !adults.Any(other => other.Id == person.Partner)).ToArray();
            if (!adults.Any(person => person.Id == chosen.Partner) && unpaired.Length > 0 && random.Next(50) == 0)
            {
                var partner = unpaired[random.Next(unpaired.Length)];
                people[first] = chosen with { Partner = partner.Id, Married = false };
                var index = people.FindIndex(item => item.Id == partner.Id);
                people[index] = partner with { Partner = chosen.Id, Married = false };
                PersonEvent(evening, $"{chosen.Name} and {partner.Name} became partners.", first, index);
            }
            else if (chosen.Partner is { } partnerId &&
                adults.FirstOrDefault(person => person.Id == partnerId && person.Partner == chosen.Id) is { } family)
            {
                var index = people.FindIndex(item => item.Id == partnerId);
                if (!chosen.Married && random.Next(120) == 0)
                {
                    people[first] = chosen with { Married = true };
                    people[index] = family with { Married = true };
                    PersonEvent(evening, $"{chosen.Name} and {family.Name} got married.", first, index);
                }
                else if (chosen.Children < 3 && family.Children < 3 && Age(chosen, date) <= 50 && Age(family, date) <= 50 &&
                    people.Count(item => item.Alive) < 44 && random.Next(175) == 0)
                {
                    var child = NewPerson(random, FreshName(), date, "Student", chosen.Location) with { Parent = chosen.Id, OtherParent = family.Id };
                    people.Add(child);
                    people[first] = chosen with { Children = chosen.Children + 1 };
                    people[index] = family with { Children = family.Children + 1 };
                    PersonEvent(evening, $"{chosen.Name} and {family.Name} welcomed {child.Name} to their family.", first, index, people.Count - 1);
                }
            }
        }
        return world with { At = target, People = people.ToArray(), History = history.ToArray(), Prices = prices };

        void Event(DateTimeOffset at, string text)
        {
            history.Enqueue(new(at, text));
            if (history.Count > 280)
                history.Dequeue();
        }

        void PersonEvent(DateTimeOffset at, string text, params int[] indexes)
        {
            Event(at, text);
            foreach (var index in indexes)
                people[index] = Remember(people[index], new(at, text));
        }

        string FreshName()
        {
            var available = Names.Where(name => !people.Any(person => person.Name == name)).ToArray();
            return available.Length > 0 ? available[(people.Count * 7) % available.Length] : Names[people.Count % Names.Length] + " " + (people.Count + 1);
        }
    }

    internal static LifeSave Act(LifeSave world, string input, Guid? person)
    {
        var words = input.Trim().ToLowerInvariant();
        if (world.At >= LastDate)
            throw new ArgumentException("This world has reached the end of its calendar.");
        var text = "";
        var hours = 1;
        if (words.StartsWith("work", StringComparison.Ordinal))
        {
            hours = 8;
            var pay = 55 + Math.Min(40, world.Study * 2) + (string.IsNullOrEmpty(world.Career) ? 0 : 13);
            world = world with { Cash = world.Cash + pay, Work = world.Work + 1 };
            text = $"{world.Player} finished {(string.IsNullOrEmpty(world.Career) ? "a work shift" : $"a shift as {world.Career.ToLowerInvariant()}")} and earned {pay}.";
        }
        else if (words.StartsWith("study", StringComparison.Ordinal))
        {
            hours = 3;
            world = world with { Study = world.Study + 1 };
            text = $"{world.Player} spent three hours studying. Skills improve future shift pay.";
        }
        else if (words is "talk" or "help" or "share meal" or "date" or "become partners" or "marry" or "adopt" or "break up")
            return SocialAction(world, words, person);
        else if (words.StartsWith("buy", StringComparison.Ordinal))
        {
            var price = Math.Round(12 * world.Prices, 2);
            if (price > world.Cash)
                throw new ArgumentException("You cannot afford groceries right now.");
            world = world with { Cash = world.Cash - price, Groceries = world.Groceries + 1 };
            text = $"{world.Player} bought groceries for {price:0.00}.";
        }
        else if (words.StartsWith("travel ", StringComparison.Ordinal))
        {
            var place = Places(world.World).FirstOrDefault(location => words[7..].Equals(location, StringComparison.OrdinalIgnoreCase)) ??
                throw new ArgumentException("Choose a place from the location directory.");
            hours = 2;
            world = world with { Location = place };
            text = $"{world.Player} travelled to {place}.";
        }
        else if (words.StartsWith("rest", StringComparison.Ordinal))
        {
            hours = 8;
            text = $"{world.Player} took an evening off.";
        }
        else if (words.StartsWith("apply ", StringComparison.Ordinal))
        {
            var job = Jobs(world.World).FirstOrDefault(item => words[6..].Equals(item, StringComparison.OrdinalIgnoreCase)) ??
                throw new ArgumentException("Choose a job from the career list.");
            if (world.Study < 2)
                throw new ArgumentException("Study twice before applying for a career.");
            if (job == world.Career)
                throw new ArgumentException("You already work in that career.");
            hours = 3;
            world = world with { Career = job };
            text = $"{world.Player} accepted a job as {job.ToLowerInvariant()}.";
        }
        else
            throw new ArgumentException("Try one of the actions shown, or type travel followed by a district or apply followed by a career.");
        world = Advance(world, world.At.AddHours(hours));
        return world with { History = [.. world.History.TakeLast(279), new(world.At, text)] };
    }

    internal static int Age(LifePerson person, DateOnly date)
    {
        var years = date.Year - person.Birth.Year;
        return date < person.Birth.AddYears(years) ? years - 1 : years;
    }

    internal static string[] Places(string world) => world.Contains("cyber", StringComparison.OrdinalIgnoreCase)
        ? ["Lower Grid", "Spire District", "Night Market"]
        : world.Contains("medieval", StringComparison.OrdinalIgnoreCase) ? ["The Keep", "Village Square", "Mill Road"]
        : ["Town Centre", "Riverside", "Market District"];

    internal static string[] Jobs(string world) => world.Contains("cyber", StringComparison.OrdinalIgnoreCase)
        ? ["Technician", "Courier", "Medtech", "Archivist"]
        : world.Contains("medieval", StringComparison.OrdinalIgnoreCase) ? ["Smith", "Baker", "Herbalist", "Scribe"]
        : ["Teacher", "Mechanic", "Shopkeeper", "Nurse", "Carpenter"];

    private static LifePerson NewPerson(Random random, string name, DateOnly born, string job, string place)
    {
        var id = new byte[16];
        random.NextBytes(id);
        return new(new Guid(id), name, Traits[random.Next(Traits.Length)], born, random.Next(76, 100), job, place, 120, 0);
    }
}
