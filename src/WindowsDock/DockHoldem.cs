using System.Security.Cryptography;

namespace WindowsDock;

internal enum HoldemRival { Mara, Ivo, June }
internal enum HoldemMove { Check, Call, Raise, Fold }

internal sealed record HoldemOpponent(HoldemRival Id, string Name, string Style, string Profile);
internal sealed record HoldemReplayStep(int BoardCount, decimal Pot, string Detail);
internal sealed record HoldemReplay(int[] Player, int[] Board, int[]? RevealedRival, HoldemReplayStep[] Steps);
internal sealed record HoldemHand(Guid Id, HoldemRival Rival, decimal Blind, decimal Stack, bool PlayerOnButton,
    decimal PlayerTotal, decimal RivalTotal, decimal PlayerStreet, decimal RivalStreet, decimal LastRaise, string[] Moves,
    bool PlayerActed = false, bool RivalActed = false, int Turn = 0, HoldemReplayStep[]? Steps = null)
{
    internal decimal Pot => PlayerTotal + RivalTotal;
}

internal readonly record struct HoldemBet(decimal Call, decimal MinimumTo, decimal MaximumTo, bool CanRaise);

internal static partial class DockCasino
{
    internal static readonly IReadOnlyList<HoldemOpponent> Opponents =
    [
        new(HoldemRival.Mara, "Mara Vale", "Tight", "Waits for strong cards, then charges for a draw."),
        new(HoldemRival.Ivo, "Ivo Reyes", "Aggressive", "Opens more pots and occasionally fires a bluff."),
        new(HoldemRival.June, "June Park", "Loose", "Likes small pots and sees more showdowns.")
    ];

    internal static HoldemOpponent Opponent(HoldemRival rival) => Opponents.First(entry => entry.Id == rival);

    internal static CasinoWallet BeginHoldem(CasinoWallet wallet, decimal blind, int[] deck, decimal? tournamentRivalStack = null)
    {
        var stack = Math.Min(decimal.Floor(wallet.Balance), tournamentRivalStack ?? blind * 100);
        if (blind < 2 || blind != decimal.Truncate(blind) || stack < 1 || (tournamentRivalStack == null && blind > stack))
            throw new ArgumentException("Set a whole-chip big blind of at least 2 chips within your balance.");
        var small = Math.Min(stack, decimal.Floor(blind / 2));
        var big = Math.Min(stack, blind);
        var player = wallet.NextPokerButton ? small : big;
        var rival = wallet.NextPokerButton ? big : small;
        var name = Opponent(wallet.Opponent).Name;
        var posted = $"Blinds · You {player:#,0} · {name} {rival:#,0}";
        var hand = new HoldemHand(Guid.NewGuid(), wallet.Opponent, blind, stack, wallet.NextPokerButton,
            player, rival, player, rival, blind, [posted], Steps: [new(0, player + rival, posted)]);
        int[] early = [deck[0], deck[2]];
        int[] late = [deck[1], deck[3]];
        var round = new CasinoRound(CasinoGame.Holdem, player, wallet.NextPokerButton ? late : early,
            wallet.NextPokerButton ? early : late, [deck[5], deck[6], deck[7], deck[9], deck[11]], Poker: hand);
        return RunHoldem(wallet with { Balance = wallet.Balance - player, NextPokerButton = !wallet.NextPokerButton },
            round, hand, wallet.NextPokerButton);
    }

    internal static HoldemBet HoldemLimits(HoldemHand hand, bool forPlayer = true)
    {
        var street = forPlayer ? hand.PlayerStreet : hand.RivalStreet;
        var facing = forPlayer ? hand.RivalStreet : hand.PlayerStreet;
        var stackLeft = hand.Stack - (forPlayer ? hand.PlayerTotal : hand.RivalTotal);
        var rivalLeft = hand.Stack - (forPlayer ? hand.RivalTotal : hand.PlayerTotal);
        var maximum = street + stackLeft;
        var minimum = Math.Min(maximum, facing + hand.LastRaise);
        return new(Math.Max(0, facing - street), minimum, maximum, maximum > facing && rivalLeft > 0);
    }

    internal static CasinoWallet ContinueHoldem(CasinoWallet wallet, Guid expectedId, int expectedTurn, HoldemMove move,
        decimal raiseTo = 0)
    {
        if (wallet.Round is not { Poker: { } hand } round || hand.Id != expectedId || hand.Turn != expectedTurn)
            throw new ArgumentException("This hand changed. Reopen Casino to see the current action.");
        if (move == HoldemMove.Fold)
            return SettleHoldem(wallet, round, Note(hand, round.Point, "You fold"), -1, "You folded");

        var amount = HoldemAmount(hand, true, move, raiseTo);
        var next = ApplyHoldem(hand, round.Point, true, move, amount);
        return RunHoldem(wallet with { Balance = wallet.Balance - amount }, round, next, false);
    }

    internal static decimal HoldemAmount(HoldemHand hand, bool player, HoldemMove move, decimal raiseTo)
    {
        var limits = HoldemLimits(hand, player);
        var street = player ? hand.PlayerStreet : hand.RivalStreet;
        return move switch
        {
            HoldemMove.Check when limits.Call == 0 => 0,
            HoldemMove.Call when limits.Call > 0 => limits.Call,
            HoldemMove.Raise when limits.CanRaise && raiseTo >= limits.MinimumTo && raiseTo <= limits.MaximumTo &&
                raiseTo == decimal.Truncate(raiseTo) => raiseTo - street,
            HoldemMove.Raise when limits.CanRaise => throw new ArgumentException($"Choose a total from {limits.MinimumTo:#,0} to {limits.MaximumTo:#,0} chips for this street."),
            _ => throw new ArgumentException("That poker action is unavailable.")
        };
    }

    private static HoldemHand ApplyHoldem(HoldemHand hand, int boardCount, bool player, HoldemMove move, decimal amount)
    {
        var name = player ? "You" : Opponent(hand.Rival).Name;
        var street = (player ? hand.PlayerStreet : hand.RivalStreet) + amount;
        var facing = player ? hand.RivalStreet : hand.PlayerStreet;
        var aggressive = move == HoldemMove.Raise;
        var line = move switch
        {
            HoldemMove.Check => player ? "You check" : $"{name} checks",
            HoldemMove.Call => $"{name} {(player ? "call" : "calls")} {amount:#,0}",
            _ => $"{name} {(facing == 0 ? player ? "bet" : "bets" : player ? "raise to" : "raises to")} {street:#,0}"
        };
        if (aggressive)
            hand = hand with { LastRaise = Math.Max(hand.LastRaise, street - facing) };
        return Note(player
            ? hand with { PlayerTotal = hand.PlayerTotal + amount, PlayerStreet = street, PlayerActed = true, RivalActed = !aggressive && hand.RivalActed }
            : hand with { RivalTotal = hand.RivalTotal + amount, RivalStreet = street, RivalActed = true, PlayerActed = !aggressive && hand.PlayerActed },
            boardCount, line);
    }

    private static CasinoWallet RunHoldem(CasinoWallet wallet, CasinoRound round, HoldemHand hand, bool playerTurn)
    {
        while (true)
        {
            if (hand.PlayerStreet == hand.RivalStreet &&
                ((hand.PlayerActed && hand.RivalActed) || hand.PlayerTotal == hand.Stack))
            {
                if (round.Point == 5)
                {
                    var player = HoldemScore([.. round.Cards, .. round.Deck]);
                    var rival = HoldemScore([.. round.Dealer, .. round.Deck]);
                    return SettleHoldem(wallet, round, hand, player.CompareTo(rival),
                        player == rival ? "Split pot" : player > rival ? "You won the showdown" : $"{Opponent(hand.Rival).Name} won the showdown",
                        reveal: true);
                }

                round = round with { Point = round.Point == 0 ? 3 : round.Point + 1 };
                hand = Note(hand with { PlayerStreet = 0, RivalStreet = 0, LastRaise = hand.Blind, PlayerActed = false, RivalActed = false },
                    round.Point, $"{HoldemStreet(round.Point)} · {Hand(round.Deck.Take(round.Point))}");
                playerTurn = !hand.PlayerOnButton;
                continue;
            }

            if (playerTurn)
                return wallet with { Round = round with { Stake = hand.PlayerTotal, Poker = hand } };
            var limits = HoldemLimits(hand, false);
            var (move, target) = RivalMove(round, hand, limits);
            if (move == HoldemMove.Fold)
            {
                var name = Opponent(hand.Rival).Name;
                return SettleHoldem(wallet, round, Note(hand, round.Point, $"{name} folds"), 1, $"{name} folded");
            }
            hand = ApplyHoldem(hand, round.Point, false, move, HoldemAmount(hand, false, move, target));
            playerTurn = true;
        }
    }

    internal static string HoldemStreet(int cards) => cards switch { 0 => "Preflop", 3 => "Flop", 4 => "Turn", _ => "River" };

    internal static (HoldemMove Move, decimal Amount) SimulatedPlayerMove(CasinoRound round, HoldemRival style)
    {
        var hand = round.Poker!;
        var reversed = hand with
        {
            Rival = style, PlayerTotal = hand.RivalTotal, RivalTotal = hand.PlayerTotal,
            PlayerStreet = hand.RivalStreet, RivalStreet = hand.PlayerStreet
        };
        return RivalMove(round with { Dealer = round.Cards }, reversed, HoldemLimits(hand));
    }

    private static (HoldemMove Move, decimal Target) RivalMove(CasinoRound round, HoldemHand hand, HoldemBet limits)
    {
        var pocket = Ranks(round.Dealer);
        var pair = pocket[0] == pocket[1];
        var known = round.Dealer.Concat(round.Deck.Take(round.Point)).ToArray();
        // Only the rival's cards and public cards are used to choose an action.
        var power = round.Point == 0
            ? (pair ? 49 + pocket[0] * 3 : pocket.Max() * 3 + pocket.Min() + (round.Dealer[0] / 13 == round.Dealer[1] / 13 ? 7 : 0))
            : PartialHoldemScore(known) / 759375 * 12 + pocket.Max() * 2 + (pair ? 7 : 0);
        var loose = hand.Rival switch { HoldemRival.Mara => -8, HoldemRival.Ivo => 4, _ => 12 };
        var pressure = Math.Min(48m, limits.Call * 100 / Math.Max(1, hand.Pot));
        if (limits.Call > hand.Blind && power + loose < (round.Point == 0 ? 27 : 17) + pressure)
            return (HoldemMove.Fold, 0);
        var roll = RandomNumberGenerator.GetInt32(100);
        var raise = hand.Rival switch
        {
            HoldemRival.Mara => power >= 68 && roll < 56,
            HoldemRival.Ivo => power >= 55 ? roll < 72 : roll < 15,
            _ => power >= 76 && roll < 38
        };
        if (limits.CanRaise && raise && limits.Call <= hand.Pot / 2)
        {
            var target = Math.Clamp(hand.PlayerStreet + decimal.Floor(hand.Pot / 2), limits.MinimumTo, limits.MaximumTo);
            return (HoldemMove.Raise, target);
        }
        return (limits.Call == 0 ? HoldemMove.Check : HoldemMove.Call, 0);
    }

    private static int PartialHoldemScore(int[] known)
    {
        if (known.Length == 7)
            return HoldemScore(known);
        if (known.Length == 5)
            return FiveCardScore(known);
        return Enumerable.Range(0, known.Length).Max(skip => FiveCardScore(known.Where((_, index) => index != skip).ToArray()));
    }

    private static HoldemHand Note(HoldemHand hand, int boardCount, string line) => hand with
    {
        Turn = hand.Turn + 1,
        Moves = [.. hand.Moves.TakeLast(95), line],
        Steps = hand.Steps == null ? null : [.. hand.Steps.TakeLast(95), new(boardCount, hand.Pot, line)]
    };

    private static CasinoWallet SettleHoldem(CasinoWallet wallet, CasinoRound round, HoldemHand hand, int outcome,
        string label, bool reveal = false)
    {
        var matched = Math.Min(hand.PlayerTotal, hand.RivalTotal);
        var refund = hand.PlayerTotal - matched;
        var payout = matched * (outcome + 1);
        var description = $"{label} · You {Hand(round.Cards)}";
        if (reveal)
            description += $" · {Opponent(hand.Rival).Name} {Hand(round.Dealer)} · Board {Hand(round.Deck)}";
        if (refund > 0)
            description += $" · {refund:#,0} uncalled chips returned";
        var finished = Note(hand, round.Point, description);
        var replay = finished.Steps is { } steps
            ? new HoldemReplay(round.Cards, round.Deck.Take(round.Point).ToArray(), reveal ? round.Dealer : null, steps)
            : null;
        return Settle(wallet with { Balance = wallet.Balance + refund }, CasinoGame.Holdem, matched, payout, description,
            hand.Rival, finished.Moves, replay);
    }
}
