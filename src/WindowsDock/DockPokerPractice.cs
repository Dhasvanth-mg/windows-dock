using System.Threading;

namespace WindowsDock;

internal readonly record struct PokerEquity(int Wins, int Ties, int Trials, bool Exact)
{
    internal double Equity => (Wins + Ties / 2d) / Trials;
}

internal static class DockPokerPractice
{
    private const string Store = "poker-practice.json";
    private static CasinoWallet Empty => new(1000, 1000, null, []);
    internal static CasinoWallet Load() => DockData.Read<CasinoWallet>(Store) ?? Empty;
    internal static CasinoWallet Update(Func<CasinoWallet, CasinoWallet> edit) =>
        DockData.Update<CasinoWallet>(Store, saved => edit(saved ?? Empty));

    internal static CasinoWallet Refill(CasinoWallet wallet)
    {
        if (wallet.Round != null)
            throw new ArgumentException("Finish the practice hand before refilling chips.");
        return wallet with { Balance = Math.Max(wallet.Balance, 1000) };
    }

    internal static PokerEquity Estimate(int[] pocket, int[] board, CancellationToken cancel)
    {
        var publicCards = pocket.Concat(board).ToArray();
        if (pocket.Length != 2 || board.Length is not (0 or 3 or 4 or 5) ||
            publicCards.Distinct().Count() != publicCards.Length || publicCards.Any(card => card is < 0 or > 51))
            throw new ArgumentException("Choose two hole cards and the public cards visible on this street.");
        var remaining = Enumerable.Range(0, 52).Except(publicCards).ToArray();
        var player = new int[7];
        var rival = new int[7];
        Array.Copy(pocket, player, 2);
        Array.Copy(board, 0, player, 2, board.Length);
        Array.Copy(board, 0, rival, 2, board.Length);
        var wins = 0;
        var ties = 0;
        var trials = 0;
        if (board.Length == 5)
        {
            var score = DockCasino.HoldemScore(player);
            for (var first = 0; first < remaining.Length - 1; first++)
            {
                cancel.ThrowIfCancellationRequested();
                rival[0] = remaining[first];
                for (var second = first + 1; second < remaining.Length; second++)
                {
                    rival[1] = remaining[second];
                    Count(score);
                }
            }
            return new(wins, ties, trials, true);
        }

        var seed = publicCards.Aggregate(319, (state, card) => unchecked(state * 33 + card));
        var random = new Random(seed);
        var missing = 5 - board.Length;
        for (var trial = 0; trial < 750; trial++)
        {
            cancel.ThrowIfCancellationRequested();
            for (var pick = 0; pick < missing + 2; pick++)
            {
                var next = random.Next(pick, remaining.Length);
                (remaining[pick], remaining[next]) = (remaining[next], remaining[pick]);
            }
            rival[0] = remaining[0];
            rival[1] = remaining[1];
            for (var index = 0; index < missing; index++)
                player[board.Length + 2 + index] = rival[board.Length + 2 + index] = remaining[index + 2];
            Count(DockCasino.HoldemScore(player));
        }
        return new(wins, ties, trials, false);

        void Count(int score)
        {
            var other = DockCasino.HoldemScore(rival);
            trials++;
            if (score > other)
                wins++;
            else if (score == other)
                ties++;
        }
    }
}
