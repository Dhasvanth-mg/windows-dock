using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace WindowsDock;

public partial class DashboardWindow
{
    private static readonly (CasinoGame Game, string Name, string Mark, string Rules, string[] Bets)[] CasinoTables =
    [
        (CasinoGame.Blackjack, "Blackjack", "21", "Hit or stand. Dealer stands on soft 17. Blackjack pays 3:2.", []),
        (CasinoGame.EuropeanRoulette, "European roulette", "0", "Single zero. A straight number pays 35:1.", ["Red", "Black", "Even", "Odd", "Number"]),
        (CasinoGame.AmericanRoulette, "American roulette", "00", "Double zero. A straight number pays 35:1.", ["Red", "Black", "Even", "Odd", "Number"]),
        (CasinoGame.Baccarat, "Baccarat", "B", "Eight decks. Banker wins have a 5% commission. Tie pays 8:1.", ["Player", "Banker", "Tie"]),
        (CasinoGame.Slots, "Slots", "7", "Three reels. Match all three symbols to win; rarer symbols pay more.", []),
        (CasinoGame.Craps, "Craps", "⚂", "Pass line only. Roll again to make your point before a seven.", []),
        (CasinoGame.SicBo, "Sic bo", "⚄", "Big or small pays 1:1 and loses on a triple. Any triple pays 24:1.", ["Small", "Big", "Any triple"]),
        (CasinoGame.Keno, "Keno", "80", "Choose four numbers from 1 to 80. Match at least two in a 20-number draw.", []),
        (CasinoGame.VideoPoker, "Video poker", "J", "9/6 Jacks or Better. Hold any cards, then draw once.", []),
        (CasinoGame.ThreeCardPoker, "Three-card poker", "3", "Ante only. Playing needs a matching wager. Dealer qualifies with queen high.", []),
        (CasinoGame.Holdem, "Texas Hold’em", "♠", "Heads-up no-limit, alternating blinds, four betting streets. Both seats start each hand with up to 100 big blinds.", [])
    ];

    private static readonly SolidColorBrush feltInk = Brush("#F4EFE4");
    private static readonly SolidColorBrush feltSoft = Brush("#B8F4EFE4");
    private static readonly SolidColorBrush cardFace = Brush("#F8F2E4");
    private static readonly SolidColorBrush cardRed = Brush("#B0323C");
    private static readonly SolidColorBrush cardBlack = Brush("#1E1B19");
    private static readonly SolidColorBrush cardBack = Brush("#8C3440");
    private static readonly Regex CardToken = new(@"(10|[A2-9JQK])([♠♥♣♦])", RegexOptions.Compiled);

    private CasinoGame? casinoPick;
    private CasinoGame casinoLast = CasinoGame.Blackjack;
    private string casinoStake = "10";
    private int casinoExtra = -1;

    private void ShowCasino() => ShowCasino(null);

    // The casino lives on the Play page now rather than in a window of its own. Opening it from Poker or Search lands
    // there with the right table chosen.
    private void ShowCasino(CasinoGame? initial)
    {
        casinoPick = initial;
        playGame = 2;
        ShowDockDashboard();
        ShowPage(DashboardPage.Play);
    }

    private FrameworkElement CasinoStage()
    {
        var wallet = DockCasino.Load();
        if (wallet.Round == null && casinoPick is { } picked)
            casinoLast = picked;
        casinoPick = null;
        string? bet = null;
        var numbers = "";
        var holds = new HashSet<int>();
        var resetArmed = false;

        var tiles = new StackPanel();
        var felt = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(18, 14, 18, 16), MinHeight = 210 };
        var controls = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        var status = Text("", 12, bad);
        status.TextWrapping = TextWrapping.Wrap;
        status.Margin = new Thickness(0, 6, 0, 0);
        var extraTabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
        var extra = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };

        var right = new StackPanel { Children = { felt, status, controls, extraTabs, extra } };
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(178) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.Children.Add(tiles);
        Grid.SetColumn(right, 2);
        layout.Children.Add(right);
        Render();
        return layout;

        void Commit(Func<CasinoWallet, CasinoWallet> change)
        {
            try
            {
                var previous = wallet.History.FirstOrDefault();
                wallet = DockCasino.Update(change);
                status.Text = "";
                holds.Clear();
                Render();
                if (!wallet.Instant && !Equals(previous, wallet.History.FirstOrDefault()) && !SystemParameters.HighContrast)
                    felt.Child?.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(.35, 1,
                        new Duration(TimeSpan.FromMilliseconds(220))));
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                status.Text = error.Message;
            }
        }

        void Render()
        {
            var round = wallet.Round;
            if (round != null)
                casinoLast = round.Game;
            var table = CasinoTables.First(item => item.Game == casinoLast);
            if (bet == null || !table.Bets.Contains(bet))
                bet = table.Bets.FirstOrDefault();
            RenderTiles(round);
            RenderFelt(table, round);
            RenderControls(table, round);
            RenderExtraTabs();
            RenderExtra();
        }

        // One line per table, like a menu down the side. While a hand is being played the other tables wait.
        void RenderTiles(CasinoRound? round)
        {
            tiles.Children.Clear();
            var heading = Text("Tables", 11, AssistantFaint);
            heading.Margin = new Thickness(8, 2, 0, 4);
            tiles.Children.Add(heading);
            foreach (var table in CasinoTables)
            {
                var current = table.Game == casinoLast;
                var locked = round != null && !current;
                var badge = new Border
                {
                    Width = 24, Height = 24, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 9, 0),
                    Background = current ? AccentFill : ControlFill,
                    Child = new TextBlock
                    {
                        Text = table.Mark, FontSize = table.Mark.Length > 1 ? 10.5 : 12.5, FontWeight = FontWeights.SemiBold,
                        Foreground = current ? onInk : AssistantSecondary, HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(badge);
                var name = Text(table.Name, 12.5, current ? AssistantInk : AssistantSecondary, current);
                name.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(name);
                var tile = PlainButton(row, table.Name, () =>
                {
                    if (wallet.Round != null || casinoLast == table.Game)
                        return;
                    casinoLast = table.Game;
                    status.Text = "";
                    Render();
                });
                tile.HorizontalContentAlignment = HorizontalAlignment.Left;
                tile.Margin = new Thickness(0, 0, 0, 1);
                tile.Padding = new Thickness(6, 4, 8, 4);
                tile.IsEnabled = !locked;
                tile.Opacity = locked ? .45 : 1;
                if (locked)
                {
                    tile.ToolTip = "Finish this hand first";
                    ToolTipService.SetShowOnDisabled(tile, true);
                }
                if (current)
                {
                    tile.Background = controlSelected;
                    AutomationProperties.SetItemStatus(tile, "Selected");
                }
                tiles.Children.Add(tile);
            }
        }

        void RenderFelt((CasinoGame Game, string Name, string Mark, string Rules, string[] Bets) table, CasinoRound? round)
        {
            felt.Background = SystemParameters.HighContrast ? SystemColors.WindowBrush : new SolidColorBrush(CasinoFelt(wallet.Theme));
            felt.BorderBrush = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : Brush("#1FFFFFFF");
            felt.BorderThickness = new Thickness(1);
            var ink = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : feltInk;
            var soft = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : feltSoft;
            var body = new StackPanel();

            var top = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            var balance = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
            balance.Children.Add(Text($"{wallet.Balance:#,0.##}", 20, ink, true));
            var chipsWord = Text(" chips", 12, soft);
            chipsWord.VerticalAlignment = VerticalAlignment.Bottom;
            chipsWord.Margin = new Thickness(0, 0, 0, 3);
            balance.Children.Add(chipsWord);
            DockPanel.SetDock(balance, Dock.Right);
            top.Children.Add(balance);
            var title = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
            title.Children.Add(Text(table.Name, 15, ink, true));
            var rules = Text(table.Rules, 11.5, soft);
            rules.TextWrapping = TextWrapping.Wrap;
            rules.Margin = new Thickness(0, 2, 0, 0);
            title.Children.Add(rules);
            top.Children.Add(title);
            body.Children.Add(top);

            var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            if (round == null)
            {
                if (wallet.History.FirstOrDefault(entry => entry.Game == table.Game) is { } last &&
                    wallet.History.FirstOrDefault()?.Game == table.Game)
                {
                    body.Children.Add(ResultRow(last.Detail, ink));
                    var net = last.Returned - last.Stake;
                    var verdict = Text(net > 0 ? $"You won {net:#,0.##} chips" : net < 0 ? $"You lost {-net:#,0.##} chips" : "Your stake came back",
                        14, SystemParameters.HighContrast ? ink : net > 0 ? good : net < 0 ? bad : ink, true);
                    verdict.Margin = new Thickness(0, 10, 0, 0);
                    body.Children.Add(verdict);
                }
                else
                    body.Children.Add(Text("Choose your stake below and place a bet.", 13, soft));
            }
            else
            {
                switch (round.Game)
                {
                    case CasinoGame.Blackjack:
                        body.Children.Add(HandRow("Dealer", [PlayingCard(round.Dealer[0]), PlayingCard(null, faceDown: true)], null, soft));
                        body.Children.Add(HandRow("You", round.Cards.Select(card => PlayingCard(card)),
                            DockCasino.BlackjackValue(round.Cards).ToString(CultureInfo.CurrentCulture), soft));
                        break;
                    case CasinoGame.ThreeCardPoker:
                        body.Children.Add(HandRow("Dealer", Enumerable.Range(0, 3).Select(_ => PlayingCard(null, faceDown: true)), null, soft));
                        body.Children.Add(HandRow("You", round.Cards.Select(card => PlayingCard(card)), null, soft));
                        break;
                    case CasinoGame.VideoPoker:
                        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
                        for (var index = 0; index < round.Cards.Length; index++)
                            row.Children.Add(HoldCard(round.Cards[index], index, soft));
                        body.Children.Add(row);
                        body.Children.Add(Spaced(Text("Click the cards to keep, then draw.", 12, soft), 0, 8));
                        break;
                    case CasinoGame.Craps:
                        body.Children.Add(Text($"The point is {round.Point}", 22, ink, true));
                        body.Children.Add(Spaced(Text($"Roll it again before a seven. {round.Stake:#,0} chips on the pass line.", 12, soft), 0, 4));
                        break;
                    case CasinoGame.Holdem when round.Poker is { } poker:
                        var hand = new StackPanel();
                        PopulatePokerHand(round, poker, hand, actions, (move, amount) => Commit(current =>
                            DockCasino.ContinueHoldem(current, poker.Id, poker.Turn, move, amount?.Invoke() ?? 0)));
                        foreach (var line in hand.Children.OfType<TextBlock>())
                            line.Foreground = soft;
                        body.Children.Add(Spaced(Text($"{DockCasino.HoldemStreet(round.Point)} · {poker.Pot:#,0} chips in the pot", 14, ink, true), 0, 0, 4));
                        body.Children.Add(hand);
                        break;
                    case CasinoGame.Holdem:
                        body.Children.Add(HandRow("You", round.Cards.Select(card => PlayingCard(card)), null, soft));
                        body.Children.Add(HandRow("Board", round.Deck.Take(round.Point).Select(card => PlayingCard(card)), null, soft));
                        break;
                    default:
                        body.Children.Add(HandRow("You", round.Cards.Select(card => PlayingCard(card)), null, soft));
                        break;
                }
                var moves = round.Game switch
                {
                    CasinoGame.Blackjack => new[] { "Hit", "Stand" },
                    CasinoGame.Craps => ["Roll"],
                    CasinoGame.VideoPoker => ["Draw"],
                    CasinoGame.Holdem when round.Poker != null => [],
                    CasinoGame.Holdem => [round.Point switch { 0 => "Flop", 3 => "Turn", 4 => "River", _ => "Showdown" }, "Fold"],
                    _ => ["Play", "Fold"]
                };
                for (var index = 0; index < moves.Length; index++)
                {
                    var move = moves[index];
                    var button = ActionButton(move, () => Commit(current => DockCasino.Continue(current, move, holds.ToHashSet())));
                    if (index == 0)
                        MarkPrimary(button, true);
                    button.Margin = new Thickness(0, 0, 8, 0);
                    actions.Children.Add(button);
                }
            }
            if (actions.Children.Count > 0)
                body.Children.Add(actions);
            felt.Child = body;
        }

        void RenderControls((CasinoGame Game, string Name, string Mark, string Rules, string[] Bets) table, CasinoRound? round)
        {
            controls.Children.Clear();
            controls.Margin = new Thickness(0, round == null ? 12 : 0, 0, 0);
            if (round != null)
                return;
            var stakeRow = new WrapPanel();
            var stakeLabel = Text(table.Game == CasinoGame.Holdem ? "Big blind" : "Stake", 12, AssistantSecondary);
            stakeLabel.VerticalAlignment = VerticalAlignment.Center;
            stakeLabel.Width = 64;
            stakeRow.Children.Add(stakeLabel);
            var custom = Field("Chips to wager", 6);
            custom.Text = casinoStake;
            custom.Width = 74;
            custom.VerticalAlignment = VerticalAlignment.Center;
            custom.TextChanged += (_, _) => casinoStake = custom.Text;
            foreach (var amount in new[] { "5", "10", "25", "50", "100" })
                stakeRow.Children.Add(ChipButton(amount, custom));
            stakeRow.Children.Add(custom);
            controls.Children.Add(stakeRow);

            if (table.Bets.Length > 0)
                controls.Children.Add(Row("Bet", Segmented("Casino bet", table.Bets, Array.IndexOf(table.Bets, bet ?? ""), index =>
                {
                    bet = table.Bets[index];
                    Render();
                })));
            if (table.Game == CasinoGame.Keno || bet == "Number")
            {
                var pick = Field(table.Game == CasinoGame.Keno ? "Four numbers, such as 7, 21, 40, 66" : "A number on the wheel", 40);
                pick.Text = numbers;
                pick.Width = 260;
                pick.TextChanged += (_, _) => numbers = pick.Text;
                controls.Children.Add(Row(table.Game == CasinoGame.Keno ? "Numbers" : "Number", pick));
            }
            if (table.Game == CasinoGame.Holdem)
            {
                var opponents = DockCasino.Opponents;
                var chosen = DockCasino.Opponent(wallet.Opponent);
                var rival = Segmented("Poker opponent", opponents.Select(item => item.Name).ToArray(),
                    opponents.ToList().IndexOf(chosen), index => Commit(current => current with { Opponent = opponents[index].Id }));
                var note = Text($"{chosen.Style} · {chosen.Profile}", 11.5, AssistantFaint);
                note.TextWrapping = TextWrapping.Wrap;
                note.Margin = new Thickness(0, 4, 0, 0);
                controls.Children.Add(Row("Opponent", new StackPanel { Children = { rival, note } }));
            }

            var go = new WrapPanel { Margin = new Thickness(64, 12, 0, 0) };
            var place = ActionButton(table.Game == CasinoGame.Holdem ? "Deal hand" : "Place bet", () => Commit(current =>
                DockCasino.Begin(current, table.Game, ParseChips(casinoStake), bet ?? "", numbers)));
            MarkPrimary(place, true);
            place.Margin = new Thickness(0, 0, 8, 0);
            go.Children.Add(place);
            if (table.Game == CasinoGame.Slots)
                go.Children.Add(QuietButton("Spin five times", () => Commit(current =>
                {
                    var amount = ParseChips(casinoStake);
                    if (amount < 1 || current.Balance < amount * 5)
                        throw new ArgumentException("Keep enough chips to cover all five spins.");
                    for (var spin = 0; spin < 5; spin++)
                        current = DockCasino.Begin(current, CasinoGame.Slots, amount, "", "");
                    return current;
                })));
            if (table.Game == CasinoGame.Holdem)
                go.Children.Add(QuietButton("Hold’em tournament", ShowPokerTournament));
            controls.Children.Add(go);
        }

        Button ChipButton(string amount, TextBox custom)
        {
            var on = casinoStake == amount;
            var face = new Grid { Width = 34, Height = 34 };
            face.Children.Add(new Ellipse
            {
                Fill = on ? AccentFill : ControlFill, Stroke = on ? AccentFill : AssistantRule, StrokeThickness = 1.5,
                StrokeDashArray = on ? null : new DoubleCollection { 2, 2 }
            });
            face.Children.Add(new TextBlock
            {
                Text = amount, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = on ? onInk : AssistantInk,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            });
            var chip = PlainButton(face, $"Stake {amount} chips", () =>
            {
                casinoStake = amount;
                custom.Text = amount;
                Render();
            });
            chip.Padding = new Thickness(0);
            chip.Margin = new Thickness(0, 0, 6, 0);
            if (on)
                AutomationProperties.SetItemStatus(chip, "Selected");
            return chip;
        }

        FrameworkElement Row(string label, FrameworkElement control)
        {
            var row = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            var caption = Text(label, 12, AssistantSecondary);
            caption.Width = 64;
            caption.VerticalAlignment = VerticalAlignment.Top;
            caption.Margin = new Thickness(0, 6, 0, 0);
            row.Children.Add(caption);
            control.HorizontalAlignment = HorizontalAlignment.Left;
            row.Children.Add(control);
            return row;
        }

        FrameworkElement HandRow(string label, IEnumerable<FrameworkElement> cards, string? total, Brush soft)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var caption = new StackPanel { Width = 58, VerticalAlignment = VerticalAlignment.Center };
            caption.Children.Add(Text(label, 12, soft));
            if (total != null)
                caption.Children.Add(Text(total, 18, SystemParameters.HighContrast ? SystemColors.WindowTextBrush : feltInk, true));
            row.Children.Add(caption);
            var hand = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var card in cards)
                hand.Children.Add(card);
            row.Children.Add(hand);
            return row;
        }

        FrameworkElement HoldCard(int card, int index, Brush soft)
        {
            var held = holds.Contains(index);
            var stack = new StackPanel();
            var face = PlayingCard(card);
            face.Margin = new Thickness(0, held ? 0 : 8, 6, held ? 8 : 0);
            stack.Children.Add(face);
            var tag = Text(held ? "HELD" : " ", 10, held ? AccentFill : soft, true);
            tag.HorizontalAlignment = HorizontalAlignment.Center;
            tag.Margin = new Thickness(0, 0, 6, 0);
            stack.Children.Add(tag);
            var button = PlainButton(stack, $"{(held ? "Release" : "Hold")} {DockCasino.Card(card)}", () =>
            {
                if (!holds.Remove(index))
                    holds.Add(index);
                Render();
            });
            button.Padding = new Thickness(0);
            button.Margin = new Thickness(0);
            if (held)
                AutomationProperties.SetItemStatus(button, "Held");
            return button;
        }

        // The finished hand, with any cards in the result drawn as cards and the rest left as words.
        FrameworkElement ResultRow(string detail, Brush ink)
        {
            var row = new WrapPanel();
            var at = 0;
            foreach (Match match in CardToken.Matches(detail))
            {
                AddWords(detail[at..match.Index]);
                var rank = Array.IndexOf("A 2 3 4 5 6 7 8 9 10 J Q K".Split(' '), match.Groups[1].Value);
                var suit = "♠♥♣♦".IndexOf(match.Groups[2].Value[0]);
                var card = PlayingCard(suit * 13 + rank, small: true);
                card.Margin = new Thickness(0, 2, 4, 2);
                row.Children.Add(card);
                at = match.Index + match.Length;
            }
            AddWords(detail[at..]);
            return row;

            void AddWords(string words)
            {
                words = words.Trim();
                if (words.Length == 0)
                    return;
                var text = Text(words, 13.5, ink);
                text.VerticalAlignment = VerticalAlignment.Center;
                text.Margin = new Thickness(0, 0, 8, 0);
                text.TextWrapping = TextWrapping.Wrap;
                row.Children.Add(text);
            }
        }

        // Statistics, history and settings sit behind three small words under the table instead of folded sections.
        void RenderExtraTabs()
        {
            extraTabs.Children.Clear();
            string[] names = ["Statistics", "History", "Table settings"];
            for (var index = 0; index < names.Length; index++)
            {
                var tab = index;
                var on = casinoExtra == tab;
                var button = QuietButton(names[tab], () =>
                {
                    casinoExtra = casinoExtra == tab ? -1 : tab;
                    resetArmed = false;
                    RenderExtraTabs();
                    RenderExtra();
                });
                button.Foreground = on ? AssistantInk : AssistantSecondary;
                if (on)
                {
                    button.FontWeight = FontWeights.SemiBold;
                    AutomationProperties.SetItemStatus(button, "Open");
                }
                button.Margin = new Thickness(0, 0, 18, 0);
                extraTabs.Children.Add(button);
            }
        }

        void RenderExtra()
        {
            extra.Children.Clear();
            switch (casinoExtra)
            {
                case 0: RenderStatistics(); break;
                case 1: RenderHistory(); break;
                case 2: RenderSettings(); break;
            }
        }

        void RenderStatistics()
        {
            var stats = CasinoStats.For(wallet);
            var overall = stats.Overall;
            extra.Children.Add(Text($"{overall.Rounds} rounds · {overall.Wins} wins · {overall.Losses} losses · {overall.Pushes} pushes", 13, AssistantInk, true));
            extra.Children.Add(Text($"{overall.Net:+#,0.##;-#,0.##;0} chips net · {stats.BestWin:#,0.##} best win · {stats.LongestStreak} longest win streak",
                12, AssistantSecondary));
            var recent = wallet.History.Where(entry => entry.Game != null).Take(120).Reverse().ToArray();
            if (recent.Length > 0)
            {
                extra.Children.Add(Spaced(Text($"Net chips over the last {recent.Length} rounds", 11, AssistantFaint), 0, 10, 0, 3));
                extra.Children.Add(CasinoProgressPlot(recent));
            }
            var milestones = stats.Milestones();
            if (milestones.Length > 0)
                extra.Children.Add(Spaced(Text(string.Join(" · ", milestones), 12, AssistantInk), 0, 8));
            foreach (var game in CasinoTables.Where(item => stats.Tables.ContainsKey(item.Game)))
            {
                var record = stats.Tables[game.Game];
                extra.Children.Add(Text($"{game.Name} · {record.Wins}W {record.Losses}L {record.Pushes}P · {record.Net:+#,0.##;-#,0.##;0} chips",
                    12, AssistantSecondary));
            }
            foreach (var opponent in DockCasino.Opponents)
                if (stats.Rivals.TryGetValue(opponent.Id, out var record))
                    extra.Children.Add(Text($"vs {opponent.Name} · {record.Wins}W {record.Losses}L {record.Pushes}P", 12, AssistantSecondary));
        }

        void RenderHistory()
        {
            if (wallet.History.Length == 0)
                extra.Children.Add(Text("Nothing played yet.", 12, AssistantSecondary));
            foreach (var entry in wallet.History.Take(30))
            {
                var net = entry.Returned - entry.Stake;
                var line = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
                var amount = Text($"{net:+#,0.##;-#,0.##;0}", 12, net > 0 ? good : net < 0 ? bad : AssistantSecondary, true);
                DockPanel.SetDock(amount, Dock.Right);
                line.Children.Add(amount);
                var detail = Text(entry.Detail, 12, AssistantInk);
                detail.TextWrapping = TextWrapping.Wrap;
                line.Children.Add(detail);
                extra.Children.Add(line);
                if (entry.Moves is not { Length: > 0 } moves)
                    continue;
                var transcript = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(12, 0, 0, 6) };
                foreach (var move in moves)
                {
                    var step = Text(move, 11.5, AssistantSecondary);
                    step.TextWrapping = TextWrapping.Wrap;
                    transcript.Children.Add(step);
                }
                var toggle = QuietButton("Replay this hand");
                toggle.Margin = new Thickness(0, -2, 0, 2);
                toggle.Click += (_, _) =>
                {
                    var open = transcript.Visibility != Visibility.Visible;
                    transcript.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
                    toggle.Content = open ? "Hide the replay" : "Replay this hand";
                };
                extra.Children.Add(toggle);
                extra.Children.Add(transcript);
            }
        }

        void RenderSettings()
        {
            var themes = new WrapPanel();
            foreach (var preset in new[] { "Midnight", "Las Vegas", "Monte Carlo", "Neon", "Medieval" })
            {
                var swatch = new StackPanel { Orientation = Orientation.Horizontal };
                swatch.Children.Add(new Ellipse { Width = 12, Height = 12, Fill = new SolidColorBrush(CasinoFelt(preset)),
                    Stroke = AssistantRule, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
                swatch.Children.Add(new TextBlock { Text = preset, VerticalAlignment = VerticalAlignment.Center });
                var choice = MakeButton(swatch, $"{preset} table", () => Commit(current => current with { Theme = preset }));
                choice.Margin = new Thickness(0, 0, 6, 6);
                if (wallet.Theme == preset)
                    choice.Tag = "Selected";
                themes.Children.Add(choice);
            }
            extra.Children.Add(SettingRow("Table colour", "Any other name picks a colour of its own.", null));
            extra.Children.Add(themes);
            var custom = Field("Your own table name", 80);
            custom.Width = 200;
            custom.Text = wallet.Theme;
            custom.KeyDown += (_, args) =>
            {
                if (args.Key == Key.Enter)
                    Commit(current => current with { Theme = DockDeskspaces.Label(custom.Text) });
            };
            extra.Children.Add(new WrapPanel { Children = { custom, QuietButton("Use this name", () =>
                Commit(current => current with { Theme = DockDeskspaces.Label(custom.Text) })) } });

            var instant = Switch("Reveal results instantly", wallet.Instant);
            instant.Checked += (_, _) => Commit(current => current with { Instant = true });
            instant.Unchecked += (_, _) => Commit(current => current with { Instant = false });
            extra.Children.Add(SettingRow("Reveal results instantly", "Skips the short fade on each result.", instant));
            var bonus = ActionButton("Claim", () => Commit(current => DockCasino.Claim(current, DateOnly.FromDateTime(DateTime.Now))));
            extra.Children.Add(SettingRow("Daily 100 chips", "Once a day.", bonus));

            var startingChips = Field("Starting chips", 6);
            startingChips.Text = "1000";
            startingChips.Width = 74;
            var reset = ActionButton(resetArmed ? "Yes, reset" : "Reset wallet", () =>
            {
                if (!resetArmed)
                {
                    resetArmed = true;
                    RenderExtra();
                    return;
                }
                resetArmed = false;
                Commit(current => DockCasino.Reset(current, ParseChips(startingChips.Text)));
            });
            if (resetArmed)
                reset.Foreground = bad;
            var resetControls = new StackPanel { Orientation = Orientation.Horizontal, Children = { startingChips, reset } };
            reset.Margin = new Thickness(6, 0, 0, 0);
            extra.Children.Add(SettingRow("Start over", resetArmed
                ? "This clears your balance, history and statistics. Click again to confirm."
                : "Sets a new balance and clears history and statistics.", resetControls, last: true));
        }
    }

    private static T Spaced<T>(T element, double left, double top, double right = 0, double bottom = 0) where T : FrameworkElement
    {
        element.Margin = new Thickness(left, top, right, bottom);
        return element;
    }

    /// <summary>A playing card: face up with its rank and suit, face down, or an empty place still to be dealt.</summary>
    private Border PlayingCard(int? card, bool faceDown = false, bool small = false)
    {
        var width = small ? 28 : 46;
        var height = small ? 38 : 64;
        var border = new Border
        {
            Width = width, Height = height, CornerRadius = new CornerRadius(small ? 4 : 6), Margin = new Thickness(0, 0, 6, 0),
            BorderThickness = new Thickness(1), SnapsToDevicePixels = true
        };
        if (SystemParameters.HighContrast)
        {
            border.Background = SystemColors.WindowBrush;
            border.BorderBrush = SystemColors.WindowTextBrush;
            if (card is { } plain)
                border.Child = new TextBlock { Text = DockCasino.Card(plain), Foreground = SystemColors.WindowTextBrush,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            return border;
        }
        if (faceDown)
        {
            border.Background = cardBack;
            border.BorderBrush = Brush("#33FFFFFF");
            border.Child = new Border
            {
                Margin = new Thickness(3), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1),
                BorderBrush = Brush("#55F8F2E4"),
                Child = new TextBlock { Text = "◆", FontSize = small ? 9 : 13, Foreground = Brush("#66F8F2E4"),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            AutomationProperties.SetName(border, "Face-down card");
            return border;
        }
        if (card is not { } value)
        {
            border.Background = Brush("#14FFFFFF");
            border.BorderBrush = Brush("#26FFFFFF");
            AutomationProperties.SetName(border, "Card still to come");
            return border;
        }
        var rank = "A 2 3 4 5 6 7 8 9 10 J Q K".Split(' ')[value % 13];
        var suit = "♠♥♣♦"[(value % 52) / 13];
        var ink = suit is '♥' or '♦' ? cardRed : cardBlack;
        border.Background = cardFace;
        border.BorderBrush = Brush("#40000000");
        var face = new Grid();
        face.Children.Add(new TextBlock
        {
            Text = rank, FontSize = small ? 10.5 : 13, FontWeight = FontWeights.Bold, Foreground = ink,
            Margin = new Thickness(small ? 3 : 5, small ? 1 : 3, 0, 0), HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        });
        face.Children.Add(new TextBlock
        {
            Text = suit.ToString(), FontSize = small ? 14 : 24, Foreground = ink,
            HorizontalAlignment = small ? HorizontalAlignment.Right : HorizontalAlignment.Center,
            VerticalAlignment = small ? VerticalAlignment.Bottom : VerticalAlignment.Center,
            Margin = small ? new Thickness(0, 0, 3, 0) : new Thickness(0, 10, 0, 0)
        });
        border.Child = face;
        AutomationProperties.SetName(border, DockCasino.Card(value));
        return border;
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
        var first = ActionButton(call, () => move(limits.Call == 0 ? HoldemMove.Check : HoldemMove.Call, null));
        MarkPrimary(first, true);
        AddMove(first);
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
        var face = PlayingCard(card);
        face.Margin = new Thickness(firstBoardCard ? 14 : 0, 0, 5, 0);
        return face;
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
        "Midnight" => Color.FromRgb(22, 30, 40),
        "Las Vegas" => Color.FromRgb(70, 30, 28),
        "Monte Carlo" => Color.FromRgb(20, 58, 44),
        "Neon" => Color.FromRgb(48, 28, 66),
        "Medieval" => Color.FromRgb(52, 42, 24),
        _ => CustomFelt(SHA256.HashData(Encoding.UTF8.GetBytes(theme)))
    };

    private static Color CustomFelt(byte[] hash) =>
        Color.FromRgb((byte)(18 + hash[0] % 40), (byte)(18 + hash[1] % 40), (byte)(18 + hash[2] % 40));
}
