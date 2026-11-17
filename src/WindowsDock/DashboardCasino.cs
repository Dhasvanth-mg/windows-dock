using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace WindowsDock;

public partial class DashboardWindow
{
    private static readonly (CasinoGame Game, string Name, string Rules, string[] Bets)[] CasinoTables =
    [
        (CasinoGame.Blackjack, "Blackjack", "Hit or stand. Dealer stands on soft 17. Blackjack pays 3:2.", []),
        (CasinoGame.EuropeanRoulette, "European roulette", "Single zero. A straight number pays 35:1.", ["Red", "Black", "Even", "Odd", "Number"]),
        (CasinoGame.AmericanRoulette, "American roulette", "Double zero. A straight number pays 35:1.", ["Red", "Black", "Even", "Odd", "Number"]),
        (CasinoGame.Baccarat, "Baccarat", "Eight decks. Banker wins have a 5% commission. Tie pays 8:1.", ["Player", "Banker", "Tie"]),
        (CasinoGame.Slots, "Three-reel slots", "Match all three symbols to win. Different symbols carry different prizes.", []),
        (CasinoGame.Craps, "Craps", "Pass line only. Roll again to make your point before a seven.", []),
        (CasinoGame.SicBo, "Sic bo", "Big or small pays 1:1 and loses on a triple. Any triple pays 24:1.", ["Small", "Big", "Any triple"]),
        (CasinoGame.Keno, "Four-spot keno", "Choose four numbers from 1 to 80. Match at least two in a 20-number draw.", []),
        (CasinoGame.VideoPoker, "Video poker", "9/6 Jacks or Better. Hold any cards, then draw once.", []),
        (CasinoGame.ThreeCardPoker, "Three-card poker", "Ante only. Playing requires a matching wager. Dealer qualifies with queen high.", []),
        (CasinoGame.Holdem, "Texas Hold’em", "Heads-up no-limit with alternating blinds and four betting streets. Both seats start each hand with the same effective stack, up to 100 big blinds.", [])
    ];

    private void ShowCasino() => ShowCasino(null);

    private void ShowCasino(CasinoGame? initial)
    {
        var (window, content, _) = CreateDockPanel("Casino", "Offline · fictional chips", 690, 0xE7FC, accent,
            backAction: initial == null ? null : ShowPokerLobby, backLabel: "Back to Poker", height: 615, expandable: true);
        var sectionStyle = (Style)window.FindResource("AppletSection");
        var wallet = DockCasino.Load();
        var table = new ComboBox { ItemsSource = CasinoTables.Select(item => item.Name),
            SelectedIndex = initial is { } selectedGame ? Array.FindIndex(CasinoTables, item => item.Game == selectedGame) : 0 };
        AutomationProperties.SetName(table, "Casino game");
        var stake = Field("Chips to wager", 6);
        stake.Text = "10";
        var option = new ComboBox();
        AutomationProperties.SetName(option, "Casino bet");
        var number = Field("Bet numbers", 40);
        var rules = Text("", 12, AssistantSecondary);
        rules.TextWrapping = TextWrapping.Wrap;
        var balance = Text("", 22, AssistantInk, strong: true);
        var outcome = Text("", 15, AssistantInk, strong: true);
        outcome.TextWrapping = TextWrapping.Wrap;
        var status = Text("", 12, AssistantSecondary);
        status.TextWrapping = TextWrapping.Wrap;
        var choices = new WrapPanel();
        var setup = new StackPanel();
        var ledger = new StackPanel();
        var hand = new StackPanel();
        var statistics = new StackPanel();
        var statsSection = new Expander { Header = "Statistics and milestones", Content = statistics, Style = sectionStyle };
        var historySection = new Expander { Header = "Play history and hand replays", Content = ledger, Style = sectionStyle };
        var surface = Card(new StackPanel { Children = { balance, outcome, hand, choices, status } });
        surface.Margin = new Thickness(0, 0, 0, 10);
        var theme = Field("Casino theme", 80);
        theme.Text = wallet.Theme;
        var instant = new CheckBox { Content = "Reveal results instantly", IsChecked = wallet.Instant };
        var start = ActionButton("Place bet");
        var rapid = QuietButton("Play 5 spins");
        var rival = new ComboBox { ItemsSource = DockCasino.Opponents, DisplayMemberPath = nameof(HoldemOpponent.Name),
            SelectedItem = DockCasino.Opponent(wallet.Opponent) };
        AutomationProperties.SetName(rival, "Poker opponent");
        var rivalNote = Text("", 12, AssistantSecondary);
        rivalNote.TextWrapping = TextWrapping.Wrap;
        var match = new StackPanel { Children = { Labeled("Opponent", rival), rivalNote } };
        var stakeLabel = Text("Stake", 12, AssistantSecondary);
        setup.Children.Add(match);
        content.Children.Add(surface);
        content.Children.Add(Labeled("Table", table));
        setup.Children.Add(rules);
        setup.Children.Add(stakeLabel);
        setup.Children.Add(stake);
        var amounts = new WrapPanel { Margin = new Thickness(0, 5, 0, 5) };
        foreach (var chips in new[] { "5", "10", "25", "50", "100" })
        {
            var chip = ActionButton(chips, () => stake.Text = chips);
            chip.Margin = new Thickness(0, 0, 5, 3);
            chip.Padding = new Thickness(8, 3, 8, 3);
            amounts.Children.Add(chip);
        }
        setup.Children.Add(amounts);
        var bet = Labeled("Bet", option);
        var numbers = Labeled("Number · four comma-separated for keno", number);
        start.Click += (_, _) =>
        {
            var selected = CasinoTables[table.SelectedIndex];
            Commit(current => DockCasino.Begin(current, selected.Game, ParseChips(stake.Text), option.SelectedItem as string ?? "", number.Text));
        };
        rapid.Click += (_, _) => Commit(current =>
        {
            var amount = ParseChips(stake.Text);
            if (amount < 1 || current.Balance < amount * 5)
                throw new ArgumentException("Keep enough chips to cover all five spins.");
            for (var spin = 0; spin < 5; spin++)
                current = DockCasino.Begin(current, CasinoGame.Slots, amount, "", "");
            return current;
        });
        setup.Children.Add(bet);
        setup.Children.Add(numbers);
        setup.Children.Add(new WrapPanel { Children = { start, rapid } });
        content.Children.Add(setup);
        var configuration = new Expander { Header = "Table settings and daily chips", Style = sectionStyle };
        var startingChips = Field("Starting chips", 6);
        startingChips.Text = "1000";
        var themes = new WrapPanel();
        foreach (var preset in new[] { "Midnight", "Las Vegas", "Monte Carlo", "Neon", "Medieval" })
            themes.Children.Add(QuietButton(preset, () => { theme.Text = preset; SaveTheme(); }));
        configuration.Content = new StackPanel { Children =
        {
            themes, Labeled("Table theme name", theme), QuietButton("Apply theme", SaveTheme), instant,
            QuietButton("Claim daily 100 chips", () => Commit(current => DockCasino.Claim(current, DateOnly.FromDateTime(DateTime.Now)))),
            Labeled("New starting balance", startingChips), QuietButton("Reset fictional wallet", () =>
            {
                if (MessageBox.Show(window, "Reset the balance and clear your play history and statistics?", "Casino",
                        MessageBoxButton.YesNo, MessageBoxImage.None, MessageBoxResult.No) == MessageBoxResult.Yes)
                    Commit(current => DockCasino.Reset(current, ParseChips(startingChips.Text)));
            })
        } };
        var tournament = ActionButton("Hold’em tournament", ShowPokerTournament);
        tournament.HorizontalAlignment = HorizontalAlignment.Left;
        tournament.Margin = new Thickness(0, 5, 0, 1);
        content.Children.Add(tournament);
        content.Children.Add(configuration);
        content.Children.Add(statsSection);
        content.Children.Add(historySection);
        statsSection.Expanded += (_, args) => { if (ReferenceEquals(args.OriginalSource, statsSection)) RenderStatistics(); };
        historySection.Expanded += (_, args) => { if (ReferenceEquals(args.OriginalSource, historySection)) RenderHistory(); };
        table.SelectionChanged += (_, _) => Configure();
        option.SelectionChanged += (_, _) => ShowNumbers();
        rival.SelectionChanged += (_, _) =>
        {
            if (rival.SelectedItem is HoldemOpponent selected && selected.Id != wallet.Opponent)
                Commit(current => current with { Opponent = selected.Id });
        };
        instant.Checked += (_, _) => SaveSpeed(true);
        instant.Unchecked += (_, _) => SaveSpeed(false);
        Render();
        window.Show();

        void SaveTheme() => Commit(current => current with { Theme = DockDeskspaces.Label(theme.Text) });
        void SaveSpeed(bool value) => Commit(current => current with { Instant = value });

        void Commit(Func<CasinoWallet, CasinoWallet> change)
        {
            try
            {
                var previous = wallet.History.FirstOrDefault();
                wallet = DockCasino.Update(change);
                status.Text = "";
                Render();
                if (!wallet.Instant && !Equals(previous, wallet.History.FirstOrDefault()) && !SystemParameters.HighContrast)
                    outcome.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(.25, 1,
                        new Duration(TimeSpan.FromMilliseconds(220))));
            }
            catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
            {
                status.Text = error.Message;
            }
        }

        void Configure()
        {
            var current = CasinoTables[table.SelectedIndex];
            var savedBet = option.SelectedItem as string;
            rules.Text = current.Rules;
            option.ItemsSource = current.Bets;
            option.SelectedItem = current.Bets.FirstOrDefault(item => item == savedBet) ?? current.Bets.FirstOrDefault();
            bet.Visibility = current.Bets.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            rapid.Visibility = current.Game == CasinoGame.Slots ? Visibility.Visible : Visibility.Collapsed;
            match.Visibility = current.Game == CasinoGame.Holdem ? Visibility.Visible : Visibility.Collapsed;
            stakeLabel.Text = current.Game == CasinoGame.Holdem ? "Big blind" : "Stake";
            start.Content = current.Game == CasinoGame.Holdem ? "Deal hand" : "Place bet";
            AutomationProperties.SetName(start, (string)start.Content);
            if (rival.SelectedItem is HoldemOpponent selected)
                rivalNote.Text = $"{selected.Style} · {selected.Profile}";
            ShowNumbers();
        }

        void ShowNumbers() => numbers.Visibility = CasinoTables[table.SelectedIndex].Game == CasinoGame.Keno ||
            option.SelectedItem as string == "Number" ? Visibility.Visible : Visibility.Collapsed;

        void Render()
        {
            balance.Text = $"{wallet.Balance:#,0.##} chips";
            if (!SystemParameters.HighContrast)
                surface.Background = new SolidColorBrush(CasinoFelt(wallet.Theme));
            choices.Children.Clear();
            hand.Children.Clear();
            var round = wallet.Round;
            table.IsEnabled = round == null;
            setup.Visibility = round == null ? Visibility.Visible : Visibility.Collapsed;
            if (round != null)
            {
                table.SelectedIndex = Array.FindIndex(CasinoTables, item => item.Game == round.Game);
                outcome.Text = round.Game switch
                {
                    CasinoGame.Blackjack => $"You  {DockCasino.Hand(round.Cards)}  ({DockCasino.BlackjackValue(round.Cards)}) · Dealer  {DockCasino.Card(round.Dealer[0])} ▪",
                    CasinoGame.Craps => $"Point is {round.Point} · {round.Stake:#,0} at stake",
                    CasinoGame.Holdem => $"You  {DockCasino.Hand(round.Cards)} · Board  {DockCasino.Hand(round.Deck.Take(round.Point))}",
                    _ => DockCasino.Hand(round.Cards)
                };
                var holds = new List<CheckBox>();
                if (round.Game == CasinoGame.VideoPoker)
                    foreach (var card in round.Cards)
                    {
                        var hold = new CheckBox { Content = "Hold " + DockCasino.Card(card), Margin = new Thickness(5) };
                        holds.Add(hold);
                        choices.Children.Add(hold);
                    }
                var actions = round.Game switch
                {
                    CasinoGame.Blackjack => new[] { "Hit", "Stand" },
                    CasinoGame.Craps => ["Roll"],
                    CasinoGame.VideoPoker => ["Draw"],
                    CasinoGame.Holdem when round.Poker != null => [],
                    CasinoGame.Holdem => [round.Point switch { 0 => "Flop", 3 => "Turn", 4 => "River", _ => "Showdown" }, "Fold"],
                    _ => ["Play", "Fold"]
                };
                foreach (var action in actions)
                    choices.Children.Add(ActionButton(action, () => Commit(current =>
                        DockCasino.Continue(current, action, holds.Select((hold, index) => (hold, index))
                            .Where(item => item.hold.IsChecked == true).Select(item => item.index).ToHashSet()))));
                if (round.Poker is { } poker)
                {
                    outcome.Text = $"{DockCasino.HoldemStreet(round.Point)} · {poker.Pot:#,0} chips in pot";
                    PopulatePokerHand(round, poker, hand, choices, (move, amount) => Commit(current =>
                        DockCasino.ContinueHoldem(current, poker.Id, poker.Turn, move, amount?.Invoke() ?? 0)));
                }
            }
            else
                outcome.Text = wallet.History.FirstOrDefault() is { } result
                    ? $"{result.Detail} · {result.Returned - result.Stake:+#,0.##;-#,0.##;0} chips"
                    : "Choose a table and place a bet.";
            Configure();
            if (statsSection.IsExpanded)
                RenderStatistics();
            if (historySection.IsExpanded)
                RenderHistory();
        }

        void RenderStatistics()
        {
            statistics.Children.Clear();
            var stats = CasinoStats.For(wallet);
            var overall = stats.Overall;
            statistics.Children.Add(Text($"{overall.Rounds} rounds · {overall.Wins} wins · {overall.Losses} losses · {overall.Pushes} pushes", 13, AssistantInk, true));
            statistics.Children.Add(Text($"{overall.Net:+#,0.##;-#,0.##;0} chips net · {stats.BestWin:#,0.##} best win · {stats.LongestStreak} longest win streak",
                12, AssistantSecondary));
            var recent = wallet.History.Where(entry => entry.Game != null).Take(120).Reverse().ToArray();
            if (recent.Length > 0)
            {
                statistics.Children.Add(Text($"Net chips over the last {recent.Length} rounds", 11, AssistantSecondary));
                statistics.Children.Add(CasinoProgressPlot(recent));
            }
            var milestones = stats.Milestones();
            if (milestones.Length > 0)
                statistics.Children.Add(Text(string.Join(" · ", milestones), 12, AssistantInk));
            foreach (var game in CasinoTables.Where(item => stats.Tables.ContainsKey(item.Game)))
            {
                var record = stats.Tables[game.Game];
                statistics.Children.Add(Text($"{game.Name} · {record.Wins}W {record.Losses}L {record.Pushes}P · {record.Net:+#,0.##;-#,0.##;0} chips",
                    12, AssistantSecondary));
            }
            foreach (var opponent in DockCasino.Opponents)
                if (stats.Rivals.TryGetValue(opponent.Id, out var record))
                    statistics.Children.Add(Text($"vs {opponent.Name} · {record.Wins}W {record.Losses}L {record.Pushes}P", 12, AssistantSecondary));
        }

        void RenderHistory()
        {
            ledger.Children.Clear();
            foreach (var entry in wallet.History.Take(30))
            {
                var row = Text($"{entry.Detail}   {entry.Returned - entry.Stake:+#,0.##;-#,0.##;0}", 12, AssistantInk);
                row.TextWrapping = TextWrapping.Wrap;
                if (entry.Moves is not { Length: > 0 } moves)
                {
                    ledger.Children.Add(row);
                    continue;
                }
                var replay = new Expander { Header = row, Style = sectionStyle };
                replay.Expanded += (_, _) =>
                {
                    if (replay.Content != null)
                        return;
                    var transcript = new StackPanel();
                    foreach (var move in moves)
                        transcript.Children.Add(new TextBlock { Text = move, FontSize = 12, Foreground = AssistantSecondary, TextWrapping = TextWrapping.Wrap });
                    replay.Content = transcript;
                };
                ledger.Children.Add(replay);
            }
        }
    }

    private void PopulatePokerHand(CasinoRound round, HoldemHand poker, StackPanel hand, WrapPanel choices,
        Action<HoldemMove, Func<decimal>?> move)
    {
        var opponent = DockCasino.Opponent(poker.Rival).Name;
        var limits = DockCasino.HoldemLimits(poker);
        hand.Children.Add(Text($"{opponent} · {poker.Stack - poker.PlayerTotal:#,0} effective chips behind · " +
            (poker.PlayerOnButton ? "You have the button" : $"{opponent} has the button"), 11, AssistantSecondary));
        var cards = new WrapPanel { Margin = new Thickness(0, 8, 0, 5) };
        foreach (var card in round.Cards)
            cards.Children.Add(PokerCard(card));
        for (var index = 0; index < 5; index++)
            cards.Children.Add(PokerCard(index < round.Point ? round.Deck[index] : null, index == 0));
        hand.Children.Add(cards);
        foreach (var detail in poker.Moves.TakeLast(2))
            hand.Children.Add(Text(detail, 12, AssistantSecondary));
        var call = limits.Call == 0 ? "Check" : $"Call {limits.Call:#,0}";
        AddMove(ActionButton(call, () => move(limits.Call == 0 ? HoldemMove.Check : HoldemMove.Call, null)));
        if (limits.CanRaise)
        {
            var raise = Field("Poker bet or raise total", 9);
            raise.Width = 80;
            raise.Text = limits.MinimumTo.ToString(CultureInfo.CurrentCulture);
            AddMove(raise);
            AddMove(ActionButton(poker.RivalStreet == 0 ? "Bet" : "Raise to", () =>
                move(HoldemMove.Raise, () => ParseChips(raise.Text))));
            AddMove(QuietButton("All in", () => move(HoldemMove.Raise, () => limits.MaximumTo)));
        }
        AddMove(QuietButton("Fold", () => move(HoldemMove.Fold, null)));

        void AddMove(FrameworkElement control)
        {
            control.Margin = new Thickness(0, 5, 9, 4);
            choices.Children.Add(control);
        }
    }

    private Border PokerCard(int? card, bool firstBoardCard = false)
    {
        var shown = card != null;
        var red = card is { } value && value / 13 is 1 or 3;
        var ink = SystemParameters.HighContrast ? SystemColors.WindowTextBrush :
            shown ? new SolidColorBrush(red ? Color.FromRgb(142, 36, 46) : Color.FromRgb(30, 27, 25)) : AssistantSecondary;
        var mark = Text(card is { } rank ? DockCasino.Card(rank) : "·", 16, ink, true);
        mark.HorizontalAlignment = HorizontalAlignment.Center;
        mark.VerticalAlignment = VerticalAlignment.Center;
        return new Border { Width = 39, Height = 50, CornerRadius = new CornerRadius(4),
            Margin = new Thickness(firstBoardCard ? 13 : 0, 0, 4, 0), Child = mark, BorderThickness = new Thickness(1),
            BorderBrush = AssistantRule, Background = SystemParameters.HighContrast ? SystemColors.WindowBrush :
                shown ? new SolidColorBrush(Color.FromRgb(248, 242, 228)) : assistantInset };
    }

    private FrameworkElement CasinoProgressPlot(CasinoEntry[] rounds)
    {
        const double width = 560;
        const double height = 81;
        var values = new decimal[rounds.Length + 1];
        for (var index = 0; index < rounds.Length; index++)
            values[index + 1] = values[index] + rounds[index].Returned - rounds[index].Stake;
        var ceiling = values.Max();
        var range = Math.Max(1, ceiling - values.Min());
        double Y(decimal net) => 6 + (height - 12) * (double)((ceiling - net) / range);
        var graph = new Canvas { Width = width, Height = height, Background = assistantInset };
        graph.Children.Add(new Line { X1 = 8, X2 = width - 8, Y1 = Y(0), Y2 = Y(0), Stroke = AssistantRule });
        graph.Children.Add(new Polyline { Stroke = AccentFill, StrokeThickness = 2,
            Points = new PointCollection(values.Select((total, index) => new Point(8 + (width - 16) * index / rounds.Length, Y(total)))) });
        return new Viewbox { Child = graph, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly,
            HorizontalAlignment = HorizontalAlignment.Left };
    }

    private static decimal ParseChips(string text) => decimal.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var chips)
        ? chips : throw new ArgumentException("Enter a whole number of chips.");

    private static Color CasinoFelt(string theme) => theme switch
    {
        "Midnight" => Color.FromRgb(22, 26, 34),
        "Las Vegas" => Color.FromRgb(56, 30, 27),
        "Monte Carlo" => Color.FromRgb(22, 47, 40),
        "Neon" => Color.FromRgb(42, 27, 57),
        "Medieval" => Color.FromRgb(43, 37, 23),
        _ => CustomFelt(SHA256.HashData(Encoding.UTF8.GetBytes(theme)))
    };

    private static Color CustomFelt(byte[] hash) =>
        Color.FromRgb((byte)(18 + hash[0] % 35), (byte)(18 + hash[1] % 35), (byte)(18 + hash[2] % 35));
}
