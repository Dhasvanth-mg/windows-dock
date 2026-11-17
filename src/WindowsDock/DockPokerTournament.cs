using System.Security.Cryptography;

namespace WindowsDock;

internal enum PokerFormat { Quick, Standard }
internal enum TournamentStage { Semifinal, Final, Champion, Eliminated, Withdrawn }
internal sealed record TournamentMedal(Guid Id, PokerFormat Format, TournamentStage Finish, bool ReachedFinal,
    int Hands, HoldemRival? Champion, DateTimeOffset Finished);
internal sealed record PokerTournament(Guid Id, PokerFormat Format, HoldemRival FirstOpponent, HoldemRival OtherFirst,
    HoldemRival OtherSecond, HoldemRival FinalOpponent, int OtherMatchHands, TournamentStage Stage,
    int TableHands, int Hands, decimal PlayerChips, decimal RivalChips, bool OnButton = true,
    CasinoRound? Hand = null, bool ReachedFinal = false, string LastResult = "", HoldemRival? WinningRival = null)
{
    internal bool Active => Stage is TournamentStage.Semifinal or TournamentStage.Final;
    internal HoldemRival Opponent => ReachedFinal ? FinalOpponent : FirstOpponent;
}

internal sealed record TournamentSave(PokerTournament? Current, TournamentMedal[] Results, CasinoEntry[]? Replays = null);

internal static class DockPokerTournament
{
    private const string Store = "poker-tournament.json";
    private static TournamentSave Empty => new(null, []);
    internal static TournamentSave Load() => DockData.Read<TournamentSave>(Store) ?? Empty;
    internal static TournamentSave Update(Func<TournamentSave, TournamentSave> edit) =>
        DockData.Update<TournamentSave>(Store, previous => edit(previous ?? Empty));

    internal static int StartingStack(PokerFormat format) => format == PokerFormat.Quick ? 120 : 300;
    internal static int HandsPerLevel(PokerFormat format) => format == PokerFormat.Quick ? 3 : 5;
    internal static int BigBlind(PokerFormat format, int hands) => 10 << Math.Min(7, hands / HandsPerLevel(format));

    internal static TournamentSave Start(TournamentSave saved, PokerFormat format)
    {
        if (saved.Current is { Active: true })
            throw new ArgumentException("Finish or withdraw from the current tournament first.");
        var seeds = Enum.GetValues<HoldemRival>();
        for (var end = seeds.Length - 1; end > 0; end--)
        {
            var next = RandomNumberGenerator.GetInt32(end + 1);
            (seeds[end], seeds[next]) = (seeds[next], seeds[end]);
        }
        var (winner, hands) = RunOtherMatch(format, seeds[1], seeds[2]);
        var stack = StartingStack(format);
        return saved with { Current = new(Guid.NewGuid(), format, seeds[0], seeds[1], seeds[2], winner, hands,
            TournamentStage.Semifinal, 0, 0, stack, stack) };
    }

    internal static TournamentSave Deal(TournamentSave saved, Guid id, int expectedHands, int[]? deck = null)
    {
        var match = Active(saved, id);
        if (match.Hand != null || match.Hands != expectedHands)
            throw new ArgumentException("The table has moved on. Reopen the tournament to continue.");
        var wallet = DockCasino.BeginHoldem(AsWallet(match), BigBlind(match.Format, match.TableHands),
            deck ?? DockCasino.Shuffle(52), match.RivalChips);
        return UpdateMatch(saved, match, wallet);
    }

    internal static TournamentSave Play(TournamentSave saved, Guid id, Guid hand, int turn, HoldemMove move, decimal raiseTo)
    {
        var match = Active(saved, id);
        return UpdateMatch(saved, match, DockCasino.ContinueHoldem(AsWallet(match), hand, turn, move, raiseTo));
    }

    internal static TournamentSave Withdraw(TournamentSave saved, Guid id)
    {
        var match = Active(saved, id);
        return Complete(saved, match with { Stage = TournamentStage.Withdrawn, Hand = null,
            LastResult = "You withdrew from the bracket." });
    }

    private static PokerTournament Active(TournamentSave saved, Guid id) =>
        saved.Current is { Active: true } current && current.Id == id ? current :
            throw new ArgumentException("This tournament is no longer active.");

    private static CasinoWallet AsWallet(PokerTournament match) =>
        new(match.PlayerChips, 0, null, [], match.Hand, NextPokerButton: match.OnButton, Opponent: match.Opponent);

    private static TournamentSave UpdateMatch(TournamentSave saved, PokerTournament match, CasinoWallet wallet)
    {
        var stack = StartingStack(match.Format);
        var openPot = wallet.Round?.Poker?.Pot ?? 0;
        var rivalChips = stack * 2 - wallet.Balance - openPot;
        var next = match with { PlayerChips = wallet.Balance, RivalChips = rivalChips, Hand = wallet.Round,
            OnButton = wallet.NextPokerButton };
        if (wallet.Round != null)
            return saved with { Current = next };
        var entry = wallet.History[0];
        saved = saved with { Replays = [entry, .. (saved.Replays ?? []).Take(79)] };
        var last = entry.Detail;
        next = next with { TableHands = match.TableHands + 1, Hands = match.Hands + 1, LastResult = last };
        if (next.PlayerChips == 0)
            return Complete(saved, next with { Stage = TournamentStage.Eliminated });
        if (next.RivalChips > 0)
            return saved with { Current = next };
        if (match.Stage == TournamentStage.Final)
            return Complete(saved, next with { Stage = TournamentStage.Champion });
        return saved with { Current = next with { Stage = TournamentStage.Final, TableHands = 0, ReachedFinal = true,
            PlayerChips = stack, RivalChips = stack, OnButton = true,
            LastResult = $"You advanced to the final. {last}" } };
    }

    private static TournamentSave Complete(TournamentSave saved, PokerTournament match)
    {
        HoldemRival? champion = null;
        if (match.Stage == TournamentStage.Eliminated)
            champion = match.ReachedFinal ? match.FinalOpponent : RunOtherMatch(match.Format, match.FirstOpponent, match.FinalOpponent).Winner;
        match = match with { WinningRival = champion };
        var result = new TournamentMedal(match.Id, match.Format, match.Stage, match.ReachedFinal, match.Hands,
            champion, DateTimeOffset.Now);
        return saved with { Current = match, Results = [result, .. saved.Results.Take(19)] };
    }

    private static (HoldemRival Winner, int Hands) RunOtherMatch(PokerFormat format, HoldemRival first, HoldemRival second)
    {
        var stack = StartingStack(format);
        var firstChips = (decimal)stack;
        var onButton = true;
        var played = 0;
        while (firstChips > 0 && firstChips < stack * 2)
        {
            var wallet = new CasinoWallet(firstChips, 0, null, [], NextPokerButton: onButton, Opponent: second);
            wallet = DockCasino.BeginHoldem(wallet, BigBlind(format, played), DockCasino.Shuffle(52), stack * 2 - firstChips);
            while (wallet.Round is { Poker: { } hand } round)
            {
                var (move, amount) = DockCasino.SimulatedPlayerMove(round, first);
                wallet = DockCasino.ContinueHoldem(wallet, hand.Id, hand.Turn, move, amount);
            }
            firstChips = wallet.Balance;
            onButton = wallet.NextPokerButton;
            played++;
        }
        return (firstChips > 0 ? first : second, played);
    }
}
