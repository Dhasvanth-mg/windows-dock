namespace WindowsDock;

internal sealed record CasinoTotals(int Rounds = 0, int Wins = 0, int Losses = 0, decimal Wagered = 0, decimal Returned = 0)
{
    internal int Pushes => Rounds - Wins - Losses;
    internal decimal Net => Returned - Wagered;

    internal CasinoTotals Add(CasinoEntry entry) => new(Rounds + 1, Wins + (entry.Returned > entry.Stake ? 1 : 0),
        Losses + (entry.Returned < entry.Stake ? 1 : 0), Wagered + entry.Stake, Returned + entry.Returned);
}

internal sealed record CasinoStats(CasinoTotals Overall, Dictionary<CasinoGame, CasinoTotals> Tables,
    Dictionary<HoldemRival, CasinoTotals> Rivals, decimal BestWin, int Streak, int LongestStreak)
{
    internal static CasinoStats For(CasinoWallet wallet)
    {
        if (wallet.Statistics is { } saved)
            return saved;
        var statistics = new CasinoStats(new(), [], [], 0, 0, 0);
        foreach (var entry in wallet.History.Reverse())
            statistics = statistics.Record(entry);
        return statistics;
    }

    internal CasinoStats Record(CasinoEntry entry)
    {
        if (entry.Game is not { } game)
            return this;
        var tables = new Dictionary<CasinoGame, CasinoTotals>(Tables)
        {
            [game] = Tables.GetValueOrDefault(game, new()).Add(entry)
        };
        var rivals = Rivals;
        if (entry.Rival is { } rival)
            rivals = new Dictionary<HoldemRival, CasinoTotals>(Rivals)
            {
                [rival] = Rivals.GetValueOrDefault(rival, new()).Add(entry)
            };
        var net = entry.Returned - entry.Stake;
        var streak = net > 0 ? Streak + 1 : net < 0 ? 0 : Streak;
        return new(Overall.Add(entry), tables, rivals, Math.Max(BestWin, net), streak, Math.Max(LongestStreak, streak));
    }

    internal string[] Milestones()
    {
        var earned = new List<string>();
        if (Overall.Rounds > 0) earned.Add("First round");
        if (Overall.Rounds >= 50) earned.Add("Table regular");
        if (Tables.Count >= 5) earned.Add("Table hopper");
        if (LongestStreak >= 3) earned.Add("Three in a row");
        if (Rivals.Values.Count(totals => totals.Wins > 0) >= 3) earned.Add("Beat the roster");
        return earned.ToArray();
    }
}
