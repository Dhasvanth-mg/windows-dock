namespace WindowsDock;

internal sealed record LifeChoice(string Command, string Label, int Hours, decimal Cost = 0);
internal sealed record LifeRelationship(Guid Id, string Name, string Kind, bool Alive);

internal static partial class DockLife
{
    internal static LifeChoice[] SocialChoices(LifeSave world, LifePerson guest)
    {
        if (!guest.Alive || guest.Location != world.Location)
            return [];
        var options = new List<LifeChoice>
        {
            new("talk", "Talk", 1),
            new("help", "Help out", 2)
        };
        var meal = Math.Round(15 * world.Prices, 2);
        if (guest.Affinity >= 2 && world.Cash >= meal)
            options.Add(new("share meal", "Share a meal", 2, meal));

        var date = DateOnly.FromDateTime(world.At.UtcDateTime);
        var datingCost = Math.Round(22 * world.Prices, 2);
        var yourPartner = world.Partner == guest.Id;
        var free = yourPartner || !world.People.Any(person => person.Id == world.Partner && person.Alive);
        var freeGuest = !world.People.Any(person => person.Id == guest.Partner && person.Alive);
        if (free && freeGuest && Age(guest, date) >= 18 && guest.Affinity >= 4 && !RelatedToPlayer(world, guest))
        {
            if (world.Cash >= datingCost)
                options.Add(new("date", "Go on a date", 2, datingCost));
            if (!yourPartner && guest.Dates >= 2)
                options.Add(new("become partners", "Become partners", 1));
        }
        if (yourPartner)
        {
            if (!world.Married && guest.Dates >= 3)
                options.Add(new("marry", "Get married", 3));
            if (world.Married && world.Cash >= 120 && world.People.Count(person => HasParent(person, world.Id)) < 3)
                options.Add(new("adopt", "Adopt a child", 4, 120));
            options.Add(new("break up", "End relationship", 1));
        }
        return options.ToArray();
    }

    private static LifeSave SocialAction(LifeSave world, string action, Guid? personId)
    {
        var guest = world.People.FirstOrDefault(person => person.Id == personId && person.Alive) ??
            throw new ArgumentException("Choose a living person first.");
        if (world.Location != guest.Location)
            throw new ArgumentException($"Travel to {guest.Location} to see {guest.Name}.");
        var choice = SocialChoices(world, guest).FirstOrDefault(item => item.Command == action) ??
            throw new ArgumentException("That choice is unavailable right now. Select the person to see their available actions.");
        var name = guest.Name;
        var message = action switch
        {
            "talk" => $"{world.Player} caught up with {name}. {Conversation(guest)}",
            "help" => $"{world.Player} helped {name} with {(guest.Job == "Student" ? "schoolwork" : "a few errands")}.",
            "share meal" => $"{world.Player} and {name} shared a meal in {guest.Location}.",
            "date" => $"{world.Player} and {name} went on a date in {guest.Location}.",
            "become partners" => $"{world.Player} and {name} became partners.",
            "marry" => $"{world.Player} and {name} got married.",
            "break up" => $"{world.Player} and {name} ended their relationship.",
            _ => ""
        };
        var rapport = action switch { "talk" => 1, "help" => 2, "share meal" or "date" => 3, "break up" => -5, _ => 0 };
        var updated = guest with { Affinity = (int)Math.Clamp((long)guest.Affinity + rapport, 0, 100),
            Dates = action == "date" ? Math.Min(guest.Dates + 1, 100) : action == "break up" ? 0 : guest.Dates };
        world = world with
        {
            Cash = world.Cash - choice.Cost,
            Partner = action == "become partners" ? guest.Id : action == "break up" ? null : world.Partner,
            Married = action == "marry" || action is not ("become partners" or "break up") && world.Married,
            People = world.People.Select(person => person.Id == guest.Id ? updated : person).ToArray()
        };
        world = Advance(world, world.At.AddHours(choice.Hours));
        Guid? childId = null;
        if (action == "adopt")
        {
            var date = DateOnly.FromDateTime(world.At.UtcDateTime);
            var random = new Random(unchecked(world.Seed + date.DayNumber * 31 + world.People.Length * 397));
            var namePool = Names.Where(candidate => !world.People.Any(person => person.Name == candidate)).ToArray();
            var childName = namePool.Length > 0 ? namePool[random.Next(namePool.Length)] : $"Brook {world.People.Length + 1}";
            var child = NewPerson(random, childName, date, "Student", world.Location) with { Parent = world.Id, OtherParent = guest.Id };
            childId = child.Id;
            world = world with { People = [.. world.People.Select(person => person.Id == guest.Id ? person with { Children = person.Children + 1 } : person), child] };
            message = $"{world.Player} and {name} welcomed {child.Name} into their family.";
        }
        var entry = new LifeEvent(world.At, message);
        return world with { History = [.. world.History.TakeLast(279), entry],
            People = world.People.Select(person => person.Id == guest.Id || person.Id == childId ? Remember(person, entry) : person).ToArray() };
    }

    internal static LifeRelationship[] Relationships(LifeSave world, Guid focus)
    {
        var edges = new Dictionary<Guid, LifeRelationship>();
        var person = world.People.FirstOrDefault(item => item.Id == focus);
        if (focus == world.Id)
        {
            foreach (var resident in world.People.Where(item => item.Affinity > 0 || item.Id == world.Partner || HasParent(item, focus)))
            {
                var family = HasParent(resident, focus);
                var romance = resident.Id == world.Partner;
                Add(resident.Id, family ? "Child" : romance ? world.Married ? "Spouse" : "Partner" : Rapport(resident), resident);
            }
        }
        else if (person != null)
        {
            if (person.Id == world.Partner || person.Affinity > 0)
                Add(world.Id, person.Id == world.Partner ? world.Married ? "Spouse" : "Partner" : Rapport(person));
            if (person.Partner is { } partner)
                Add(partner, person.Married ? "Spouse" : "Partner");
            if (person.Parent is { } parent)
                Add(parent, "Parent");
            if (person.OtherParent is { } other)
                Add(other, "Parent");
            foreach (var relative in world.People.Where(item => item.Id != focus))
            {
                if (HasParent(relative, focus))
                    Add(relative.Id, "Child", relative);
                else if (ShareParent(person, relative) && !edges.ContainsKey(relative.Id))
                    Add(relative.Id, "Sibling", relative);
            }
        }
        return edges.Values.OrderBy(item => item.Kind is "Parent" or "Child" or "Spouse" or "Partner" or "Sibling" ? 0 : 1)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();

        void Add(Guid id, string kind, LifePerson? known = null)
        {
            if (id == focus)
                return;
            if (id == world.Id)
                edges[id] = new(id, world.Player, kind, true);
            else if ((known ?? world.People.FirstOrDefault(item => item.Id == id)) is { } resident)
                edges[id] = new(id, resident.Name, kind, resident.Alive);
        }
    }

    internal static string Rapport(LifePerson person) => person.Dates > 0 ? "Dating" :
        person.Affinity >= 10 ? "Close friend" : person.Affinity >= 4 ? "Friend" : "Acquaintance";

    private static string Conversation(LifePerson person) => person.Trait switch
    {
        "curious" => $"{person.Name} wanted to hear what you were learning lately.",
        "restless" => $"{person.Name} talked about what might be worth trying next.",
        "methodical" => $"{person.Name} shared a plan for the week ahead.",
        "outgoing" => $"{person.Name} had news from around the district.",
        _ => $"{person.Name} asked how things had been going."
    };

    private static LifePerson Remember(LifePerson person, LifeEvent entry) =>
        person with { Memories = [.. (person.Memories ?? []).TakeLast(11), entry] };

    private static bool HasParent(LifePerson person, Guid parent) => person.Parent == parent || person.OtherParent == parent;

    private static bool ShareParent(LifePerson a, LifePerson b) =>
        (a.Parent is { } first && HasParent(b, first)) || (a.OtherParent is { } second && HasParent(b, second));

    private static bool Related(LifePerson a, LifePerson b) => HasParent(a, b.Id) || HasParent(b, a.Id) || ShareParent(a, b);

    private static bool RelatedToPlayer(LifeSave world, LifePerson person)
    {
        var pending = new Stack<LifePerson>();
        var seen = new HashSet<Guid>();
        pending.Push(person);
        while (pending.TryPop(out var current))
        {
            if (!seen.Add(current.Id))
                continue;
            if (HasParent(current, world.Id))
                return true;
            foreach (var parent in world.People.Where(item => HasParent(current, item.Id)))
                pending.Push(parent);
        }
        return false;
    }
}
