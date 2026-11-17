using System.Globalization;
using System.Security.Cryptography;

namespace WindowsDock;

internal enum CasinoGame { Blackjack, EuropeanRoulette, AmericanRoulette, Baccarat, Slots, Craps, SicBo, Keno, VideoPoker, ThreeCardPoker, Holdem }
internal sealed record CasinoRound(CasinoGame Game, decimal Stake, int[] Cards, int[] Dealer, int[] Deck, int Point = 0);
internal sealed record CasinoEntry(CasinoGame? Game, decimal Stake, decimal Returned, decimal Balance, string Detail, DateTimeOffset At);
internal sealed record CasinoWallet(decimal Balance, decimal Initial, DateOnly? Bonus, CasinoEntry[] History,
    CasinoRound? Round = null, string Theme = "Midnight", bool Instant = false);

internal static class DockCasino
{
    private const string Store = "casino.json";
    private static CasinoWallet Default => new(1000, 1000, null, []);
    private static readonly HashSet<int> Red = [1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36];
    internal static CasinoWallet Load() => DockData.Read<CasinoWallet>(Store) ?? Default;
    internal static CasinoWallet Update(Func<CasinoWallet, CasinoWallet> change) =>
        DockData.Update<CasinoWallet>(Store, saved => change(saved ?? Default));

    internal static CasinoWallet Begin(CasinoWallet wallet, CasinoGame game, decimal stake, string option, string number)
    {
        if (wallet.Round != null)
            throw new ArgumentException("Finish the active round first.");
        if (stake < 1 || stake > 1000 || stake != decimal.Truncate(stake) || stake > wallet.Balance)
            throw new ArgumentException("Choose a whole-chip stake from 1 to 1,000 within your balance.");
        wallet = wallet with { Balance = wallet.Balance - stake };
        var deck = game is CasinoGame.Blackjack or CasinoGame.VideoPoker or CasinoGame.ThreeCardPoker or CasinoGame.Holdem ? Shuffle(52) : [];
        return game switch
        {
            CasinoGame.Blackjack => Blackjack(wallet, new(game, stake, [deck[0], deck[2]], [deck[1], deck[3]], deck[4..])),
            CasinoGame.VideoPoker => wallet with { Round = new(game, stake, deck[..5], [], deck[5..]) },
            CasinoGame.ThreeCardPoker => wallet with { Round = new(game, stake, deck[..3], deck[3..6], []) },
            CasinoGame.Holdem => wallet with { Round = new(game, stake, [deck[0], deck[2]], [deck[1], deck[3]], [deck[5], deck[6], deck[7], deck[9], deck[11]]) },
            CasinoGame.Craps => RollCraps(wallet, new(game, stake, [], [], [])),
            CasinoGame.EuropeanRoulette or CasinoGame.AmericanRoulette => Spin(wallet, game, stake, option, number),
            CasinoGame.Baccarat => RunBaccarat(wallet, stake, option),
            CasinoGame.Slots => SpinSlots(wallet, stake),
            CasinoGame.SicBo => RunSicBo(wallet, stake, option),
            CasinoGame.Keno => RunKeno(wallet, stake, number),
            _ => throw new ArgumentException("Choose a casino game.")
        };
    }

    internal static CasinoWallet Continue(CasinoWallet wallet, string action, IReadOnlySet<int> held)
    {
        var round = wallet.Round ?? throw new ArgumentException("Start a round first.");
        return (round.Game, action) switch
        {
            (CasinoGame.Blackjack, "Hit") => BlackjackHit(wallet, round),
            (CasinoGame.Blackjack, "Stand") => BlackjackStand(wallet, round),
            (CasinoGame.VideoPoker, "Draw") => DrawPoker(wallet, round, held),
            (CasinoGame.ThreeCardPoker, "Fold") => Settle(wallet, round.Game, round.Stake, 0, "Folded · " + Hand(round.Cards)),
            (CasinoGame.ThreeCardPoker, "Play") => PlayThreeCard(wallet, round),
            (CasinoGame.Craps, "Roll") => RollCraps(wallet, round),
            (CasinoGame.Holdem, "Flop") when round.Point == 0 => wallet with { Round = round with { Point = 3 } },
            (CasinoGame.Holdem, "Turn") when round.Point == 3 => wallet with { Round = round with { Point = 4 } },
            (CasinoGame.Holdem, "River") when round.Point == 4 => wallet with { Round = round with { Point = 5 } },
            (CasinoGame.Holdem, "Showdown") when round.Point == 5 => Showdown(wallet, round),
            (CasinoGame.Holdem, "Fold") => Settle(wallet, round.Game, round.Stake, 0, "Folded · " + Hand(round.Cards)),
            _ => throw new ArgumentException("That action is unavailable for this round.")
        };
    }

    internal static CasinoWallet Claim(CasinoWallet wallet, DateOnly today)
    {
        if (wallet.Bonus != null && wallet.Bonus >= today)
            throw new ArgumentException("Today's bonus has already been claimed.");
        return Settle(wallet with { Bonus = today }, null, 0, 100, "Daily 100-chip bonus") with { Round = wallet.Round };
    }

    internal static CasinoWallet Reset(CasinoWallet wallet, decimal amount)
    {
        if (wallet.Round != null || amount < 100 || amount > 100_000 || decimal.Truncate(amount) != amount)
            throw new ArgumentException("Finish any open round and enter a starting balance from 100 to 100,000 chips.");
        return wallet with { Initial = amount, Balance = amount, History = [] };
    }

    private static CasinoWallet Settle(CasinoWallet wallet, CasinoGame? game, decimal stake, decimal payout, string result)
    {
        var balance = wallet.Balance + payout;
        var entry = new CasinoEntry(game, stake, payout, balance, result, DateTimeOffset.Now);
        return wallet with { Balance = balance, Round = null, History = [entry, .. wallet.History.Take(499)] };
    }

    internal static int BlackjackValue(IEnumerable<int> cards)
    {
        var values = cards.Select(card => Math.Min(10, card % 13 + 1)).ToArray();
        var total = values.Sum();
        return values.Contains(1) && total <= 11 ? total + 10 : total;
    }

    private static CasinoWallet Blackjack(CasinoWallet wallet, CasinoRound round)
    {
        var player = BlackjackValue(round.Cards) == 21;
        var dealer = BlackjackValue(round.Dealer) == 21;
        return player || dealer ? Settle(wallet, round.Game, round.Stake, round.Stake * (player == dealer ? 1 : player ? 2.5m : 0),
            BlackjackSummary(round, player && dealer ? "Push" : player ? "Blackjack" : "Dealer blackjack")) : wallet with { Round = round };
    }

    private static CasinoWallet BlackjackHit(CasinoWallet wallet, CasinoRound round)
    {
        round = round with { Cards = [.. round.Cards, round.Deck[0]], Deck = round.Deck[1..] };
        return BlackjackValue(round.Cards) > 21 ? Settle(wallet, round.Game, round.Stake, 0, BlackjackSummary(round, "Bust")) :
            BlackjackValue(round.Cards) == 21 ? BlackjackStand(wallet, round) : wallet with { Round = round };
    }

    private static CasinoWallet BlackjackStand(CasinoWallet wallet, CasinoRound round)
    {
        var index = 0;
        var dealer = round.Dealer.ToList();
        while (BlackjackValue(dealer) < 17)
            dealer.Add(round.Deck[index++]);
        round = round with { Dealer = dealer.ToArray() };
        var player = BlackjackValue(round.Cards);
        var value = BlackjackValue(dealer);
        var paid = player == value ? 1 : value > 21 || player > value ? 2 : 0;
        return Settle(wallet, round.Game, round.Stake, round.Stake * paid,
            BlackjackSummary(round, paid == 2 ? "Won" : paid == 1 ? "Push" : "Dealer wins"));
    }

    private static string BlackjackSummary(CasinoRound round, string title) =>
        $"{title} · You {Hand(round.Cards)} ({BlackjackValue(round.Cards)}) · Dealer {Hand(round.Dealer)} ({BlackjackValue(round.Dealer)})";

    private static CasinoWallet Showdown(CasinoWallet wallet, CasinoRound round)
    {
        var player = HoldemScore([.. round.Cards, .. round.Deck]);
        var dealer = HoldemScore([.. round.Dealer, .. round.Deck]);
        var result = player == dealer ? "Split pot" : player > dealer ? "Won" : "House wins";
        return Settle(wallet, round.Game, round.Stake, round.Stake * (player == dealer ? 1 : player > dealer ? 2 : 0),
            $"{result} · You {Hand(round.Cards)} · House {Hand(round.Dealer)} · Board {Hand(round.Deck)}");
    }

    internal static int HoldemScore(int[] cards)
    {
        var best = 0;
        for (var first = 0; first < cards.Length - 1; first++)
            for (var second = first + 1; second < cards.Length; second++)
                best = Math.Max(best, FiveCardScore(cards.Where((_, index) => index != first && index != second).ToArray()));
        return best;
    }

    private static int FiveCardScore(int[] cards)
    {
        var ranks = Ranks(cards).OrderDescending().ToArray();
        var groups = ranks.GroupBy(rank => rank).OrderByDescending(group => group.Count())
            .ThenByDescending(group => group.Key).ToArray();
        var flush = cards.Select(card => card / 13).Distinct().Count() == 1;
        var straight = Straight(ranks) ? ranks[0] == 14 && ranks[1] == 5 ? 5 : ranks[0] : 0;
        var category = (flush, straight > 0, groups[0].Count(), groups[1].Count()) switch
        {
            (true, true, _, _) => 8,
            (_, _, 4, _) => 7,
            (_, _, 3, 2) => 6,
            (true, _, _, _) => 5,
            (_, true, _, _) => 4,
            (_, _, 3, _) => 3,
            (_, _, 2, 2) => 2,
            (_, _, 2, _) => 1,
            _ => 0
        };
        var ordered = category is 4 or 8 ? new[] { straight } : groups.Select(group => group.Key).ToArray();
        var score = category;
        for (var index = 0; index < 5; index++)
            score = score * 15 + (index < ordered.Length ? ordered[index] : 0);
        return score;
    }

    private static CasinoWallet Spin(CasinoWallet wallet, CasinoGame game, decimal stake, string option, string number)
    {
        var american = game == CasinoGame.AmericanRoulette;
        var result = RandomNumberGenerator.GetInt32(american ? 38 : 37);
        var straight = number.Trim() == "00" ? american ? 37 : -1 : int.TryParse(number, out var n) && n is >= 0 and <= 36 ? n : -1;
        if (option == "Number" && straight < 0)
            throw new ArgumentException(american ? "Enter a number from 0 to 36 or 00." : "Enter a number from 0 to 36.");
        var win = option switch
        {
            "Number" => straight == result,
            "Red" => Red.Contains(result),
            "Black" => result is > 0 and <= 36 && !Red.Contains(result),
            "Even" => result is > 0 and <= 36 && result % 2 == 0,
            "Odd" => result is > 0 and <= 36 && result % 2 != 0,
            _ => throw new ArgumentException("Choose a roulette bet.")
        };
        return Settle(wallet, game, stake, win ? stake * (option == "Number" ? 36 : 2) : 0,
            $"{(result == 37 ? "00" : result.ToString(CultureInfo.InvariantCulture))} · {(result is 0 or 37 ? "Green" : Red.Contains(result) ? "Red" : "Black")} · {option}");
    }

    internal static bool BankerDraws(int value, int? playerThird) => playerThird switch
    {
        null => value <= 5,
        _ when value <= 2 => true,
        _ when value == 3 => playerThird != 8,
        >= 2 and <= 7 when value == 4 => true,
        >= 4 and <= 7 when value == 5 => true,
        6 or 7 when value == 6 => true,
        _ => false
    };

    private static CasinoWallet RunBaccarat(CasinoWallet wallet, decimal stake, string option)
    {
        static int Pip(int card) => (card % 13 + 1) % 13 is var r && r <= 9 ? r : 0;
        var shoe = Shuffle(416);
        var player = new List<int> { shoe[0], shoe[2] };
        var banker = new List<int> { shoe[1], shoe[3] };
        int Value(IEnumerable<int> hand) => hand.Sum(Pip) % 10;
        if (Value(player) < 8 && Value(banker) < 8)
        {
            var next = 4;
            if (Value(player) <= 5)
                player.Add(shoe[next++]);
            if (BankerDraws(Value(banker), player.Count == 3 ? Pip(player[2]) : null))
                banker.Add(shoe[next]);
        }
        var winner = Value(player) == Value(banker) ? "Tie" : Value(player) > Value(banker) ? "Player" : "Banker";
        if (option is not ("Player" or "Banker" or "Tie"))
            throw new ArgumentException("Choose Player, Banker, or Tie.");
        var payout = winner == option ? option switch { "Banker" => 1.95m, "Tie" => 9m, _ => 2m } : winner == "Tie" ? 1m : 0m;
        return Settle(wallet, CasinoGame.Baccarat, stake, stake * payout,
            $"{winner} · Player {Hand(player)} ({Value(player)}) · Banker {Hand(banker)} ({Value(banker)})");
    }

    private static CasinoWallet SpinSlots(CasinoWallet wallet, decimal stake)
    {
        string[] symbols = ["Cherry", "Lemon", "Bell", "Bar", "Seven"];
        int[] prizes = [5, 8, 20, 50, 250];
        var reel = Enumerable.Range(0, 3).Select(_ => RandomNumberGenerator.GetInt32(100) switch
            { < 50 => 0, < 75 => 1, < 90 => 2, < 98 => 3, _ => 4 }).ToArray();
        var payout = reel.Distinct().Count() == 1 ? prizes[reel[0]] : 0;
        return Settle(wallet, CasinoGame.Slots, stake, stake * payout, string.Join("  ·  ", reel.Select(value => symbols[value])));
    }

    private static int Die() => RandomNumberGenerator.GetInt32(1, 7);

    internal static (int Point, int Payout) PassLine(int point, int total) => point == 0
        ? total switch { 7 or 11 => (0, 2), 2 or 3 or 12 => (0, 0), _ => (total, -1) }
        : total == point ? (0, 2) : total == 7 ? (0, 0) : (point, -1);

    private static CasinoWallet RollCraps(CasinoWallet wallet, CasinoRound round)
    {
        int[] dice = [Die(), Die()];
        var (point, paid) = PassLine(round.Point, dice.Sum());
        return paid < 0 ? wallet with { Round = round with { Point = point, Cards = dice } } :
            Settle(wallet, CasinoGame.Craps, round.Stake, round.Stake * paid, $"Rolled {dice[0]} + {dice[1]} · {(paid > 0 ? "Pass line wins" : "Pass line loses")}");
    }

    private static CasinoWallet RunSicBo(CasinoWallet wallet, decimal stake, string option)
    {
        int[] dice = [Die(), Die(), Die()];
        var triple = dice.Distinct().Count() == 1;
        var sum = dice.Sum();
        var payout = option switch
        {
            "Small" => !triple && sum <= 10 ? 2 : 0,
            "Big" => !triple && sum >= 11 ? 2 : 0,
            "Any triple" => triple ? 25 : 0,
            _ => throw new ArgumentException("Choose Small, Big, or Any triple.")
        };
        return Settle(wallet, CasinoGame.SicBo, stake, stake * payout, $"{string.Join(" + ", dice)} = {sum} · {option}");
    }

    private static CasinoWallet RunKeno(CasinoWallet wallet, decimal stake, string input)
    {
        var numbers = input.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(number => int.TryParse(number, out var parsed) ? parsed : -1).ToArray();
        if (numbers.Length != 4 || numbers.Distinct().Count() != 4 || numbers.Any(number => number is < 1 or > 80))
            throw new ArgumentException("Pick four different numbers from 1 to 80, separated by spaces or commas.");
        var draw = Shuffle(80).Take(20).Select(number => number + 1).Order().ToArray();
        var matches = numbers.Intersect(draw).Count();
        int[] payout = [0, 0, 1, 5, 100];
        return Settle(wallet, CasinoGame.Keno, stake, payout[matches] * stake, $"{matches}/4 matched · Draw: {string.Join(", ", draw)}");
    }

    private static CasinoWallet DrawPoker(CasinoWallet wallet, CasinoRound round, IReadOnlySet<int> held)
    {
        var next = 0;
        var cards = round.Cards.Select((card, index) => held.Contains(index) ? card : round.Deck[next++]).ToArray();
        var (name, amount) = VideoHand(cards);
        return Settle(wallet, round.Game, round.Stake, amount * round.Stake, $"{name} · {Hand(cards)}");
    }

    internal static (string Name, int Returned) VideoHand(int[] cards)
    {
        var ranks = Ranks(cards);
        var flush = cards.Select(card => card / 13).Distinct().Count() == 1;
        var straight = Straight(ranks);
        var sizes = ranks.GroupBy(rank => rank).Select(group => group.Count()).OrderDescending().ToArray();
        if (flush && straight) return ranks.Min() == 10 ? ("Royal flush", 800) : ("Straight flush", 50);
        if (sizes[0] == 4) return ("Four of a kind", 25);
        if (sizes[0] == 3 && sizes[1] == 2) return ("Full house", 9);
        if (flush) return ("Flush", 6);
        if (straight) return ("Straight", 4);
        if (sizes[0] == 3) return ("Three of a kind", 3);
        if (sizes[0] == 2 && sizes[1] == 2) return ("Two pair", 2);
        if (ranks.GroupBy(rank => rank).Any(group => group.Key >= 11 && group.Count() == 2)) return ("Jacks or better", 1);
        return ("No payout", 0);
    }

    private static CasinoWallet PlayThreeCard(CasinoWallet wallet, CasinoRound round)
    {
        if (wallet.Balance < round.Stake)
            throw new ArgumentException("You need chips equal to the ante to play. Folding is available.");
        var player = ThreeCard(round.Cards);
        var dealer = ThreeCard(round.Dealer);
        var qualified = dealer.Category > 0 || Ranks(round.Dealer).Max() >= 12;
        var compared = player.Score.CompareTo(dealer.Score);
        var net = !qualified ? 3 : compared > 0 ? 4 : compared == 0 ? 2 : 0;
        var bonus = player.Category switch { 3 => 1, 4 => 4, 5 => 5, _ => 0 };
        return Settle(wallet with { Balance = wallet.Balance - round.Stake }, round.Game, round.Stake * 2,
            (net + bonus) * round.Stake, $"{Hand(round.Cards)} vs {Hand(round.Dealer)} · {(!qualified ? "Dealer does not qualify" : compared > 0 ? "Won" : compared == 0 ? "Push" : "Dealer wins")}");
    }

    private static (int Category, int Score) ThreeCard(int[] cards)
    {
        var ranks = Ranks(cards);
        var flush = cards.Select(card => card / 13).Distinct().Count() == 1;
        var straight = Straight(ranks);
        var groups = ranks.GroupBy(rank => rank).OrderByDescending(group => group.Count()).ThenByDescending(group => group.Key).ToArray();
        var category = straight && flush ? 5 : groups[0].Count() == 3 ? 4 : straight ? 3 : flush ? 2 : groups[0].Count() == 2 ? 1 : 0;
        var order = straight && ranks.Contains(14) && ranks.Contains(2) ? new[] { 3, 2, 1 } :
            groups.SelectMany(group => group).ToArray();
        return (category, ((category * 15 + order[0]) * 15 + order[1]) * 15 + order[2]);
    }

    private static bool Straight(int[] ranks) => ranks.Distinct().Count() == ranks.Length &&
        (ranks.Max() - ranks.Min() == ranks.Length - 1 || ranks.Max() == 14 && ranks.Min() == 2 &&
            ranks.Where(rank => rank != 14).Max() == ranks.Length);
    private static int[] Ranks(IEnumerable<int> cards) => cards.Select(card => card % 13 == 0 ? 14 : card % 13 + 1).ToArray();
    internal static string Card(int card) => $"{("A 2 3 4 5 6 7 8 9 10 J Q K".Split(' ')[card % 13])}{"♠♥♣♦"[(card % 52) / 13]}";
    internal static string Hand(IEnumerable<int> cards) => string.Join("  ", cards.Select(Card));

    private static int[] Shuffle(int count)
    {
        var deck = Enumerable.Range(0, count).ToArray();
        for (var index = deck.Length - 1; index > 0; index--)
        {
            var swap = RandomNumberGenerator.GetInt32(index + 1);
            (deck[index], deck[swap]) = (deck[swap], deck[index]);
        }
        return deck;
    }
}
