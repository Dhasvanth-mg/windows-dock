using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;

namespace WindowsDock;

public partial class DashboardWindow
{
    private void ShowPokerLobby()
    {
        var (window, content, _) = CreateDockPanel("Poker", "", 550, 0xE7FC, accent, height: 420);
        var cash = DockCasino.Load();
        var practice = DockPokerPractice.Load();
        var tournament = DockPokerTournament.Load().Current;
        var routes = new (string Name, string Detail, Action Open)[]
        {
            ("Cash Hold’em", cash.Round is { Game: CasinoGame.Holdem } ? "Resume your hand" : $"{cash.Balance:#,0.##} chips · heads-up", () => ShowCasino(CasinoGame.Holdem)),
            ("Tournament", tournament is { Active: true } active ? $"Resume {active.Stage.ToString().ToLowerInvariant()}" : "Four-player bracket · rising blinds", () => ShowPokerTournament(true)),
            ("Practice", practice.Round != null ? "Resume your practice hand" : "Separate chips · equity calculator", ShowPokerPractice),
            ("Hand review", "Replay completed hands from all three modes", ShowPokerReview)
        };
        foreach (var route in routes)
        {
            var button = ActionButton(route.Name, route.Open);
            button.Content = new StackPanel { Children = { Text(route.Name, 15, AssistantInk, true), Text(route.Detail, 12, AssistantSecondary) } };
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Padding = new Thickness(12, 8, 12, 8);
            button.Margin = new Thickness(0, 0, 0, 6);
            content.Children.Add(button);
        }
        window.Show();
    }

    private void ShowPokerPractice()
    {
        var (window, content, closed) = CreateDockPanel("Hold’em practice", "Separate fictional chips", 690, 0xE7FC, accent,
            backAction: ShowPokerLobby, backLabel: "Back to Poker", height: 560, expandable: true);
        var wallet = DockPokerPractice.Load();
        var setup = new StackPanel();
        var stage = new StackPanel();
        var status = Text("", 12, AssistantSecondary);
        status.TextWrapping = TextWrapping.Wrap;
        var rival = new ComboBox { ItemsSource = DockCasino.Opponents, DisplayMemberPath = nameof(HoldemOpponent.Name),
            SelectedItem = DockCasino.Opponent(wallet.Opponent) };
        AutomationProperties.SetName(rival, "Practice opponent");
        var deal = ActionButton("Deal practice hand");
        var refill = QuietButton("Refill practice chips");
        setup.Children.Add(Labeled("Opponent", rival));
        setup.Children.Add(new WrapPanel { Children = { deal, refill } });
        content.Children.Add(stage);
        content.Children.Add(setup);
        content.Children.Add(status);
        deal.Click += (_, _) => Save(current =>
        {
            if (current.Round != null)
                throw new ArgumentException("Finish the current practice hand.");
            var choice = (HoldemOpponent)rival.SelectedItem;
            return DockCasino.BeginHoldem(current with { Opponent = choice.Id }, 10, DockCasino.Shuffle(52));
        });
        refill.Click += (_, _) => Save(DockPokerPractice.Refill);
        Render();
        window.Show();

        void Save(Func<CasinoWallet, CasinoWallet> edit)
        {
            try
            {
                wallet = DockPokerPractice.Update(edit);
                status.Text = "";
                Render();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
            { status.Text = error.Message; }
        }

        void Render()
        {
            stage.Children.Clear();
            var table = new StackPanel { Children = { Text($"{wallet.Balance:#,0} practice chips", 19, AssistantInk, true) } };
            stage.Children.Add(Card(table));
            setup.Visibility = wallet.Round == null ? Visibility.Visible : Visibility.Collapsed;
            if (wallet.Round is not { Poker: { } poker } round)
            {
                var result = Text(wallet.History.FirstOrDefault()?.Detail ?? "Play a heads-up hand with a 10-chip big blind.", 12, AssistantSecondary);
                result.TextWrapping = TextWrapping.Wrap;
                table.Children.Add(result);
                return;
            }
            table.Children.Add(Text($"{DockCasino.HoldemStreet(round.Point)} · {poker.Pot:#,0} in pot", 14, AssistantInk, true));
            var hand = new StackPanel();
            var choices = new WrapPanel();
            PopulatePokerHand(round, poker, hand, choices, (move, amount) => Save(current =>
                DockCasino.ContinueHoldem(current, poker.Id, poker.Turn, move, amount?.Invoke() ?? 0)));
            table.Children.Add(hand);
            table.Children.Add(choices);
            var call = DockCasino.HoldemLimits(poker).Call;
            var potOdds = call > 0 ? $"Pot odds for this call · {call / (poker.Pot + call):P1} break-even equity if betting ends here." :
                "Checking costs zero chips.";
            table.Children.Add(Text(potOdds, 12, AssistantSecondary));
            var resultText = Text("", 12, AssistantInk);
            resultText.TextWrapping = TextWrapping.Wrap;
            var calculate = QuietButton("Estimate showdown equity");
            calculate.Click += async (_, _) =>
            {
                calculate.IsEnabled = false;
                resultText.Text = "Calculating from your cards and the visible board…";
                var publicBoard = round.Deck.Take(round.Point).ToArray();
                try
                {
                    var result = await Task.Run(() => DockPokerPractice.Estimate(round.Cards, publicBoard, closed), closed);
                    if (closed.IsCancellationRequested || wallet.Round?.Poker is not { } current ||
                        current.Id != poker.Id || current.Turn != poker.Turn)
                        return;
                    resultText.Text = $"{result.Equity:P1} showdown equity against random hole cards · " +
                        (result.Exact ? $"All {result.Trials:N0} combinations" : $"{result.Trials:N0} simulated runouts");
                }
                catch (OperationCanceledException) when (closed.IsCancellationRequested) { }
                finally { calculate.IsEnabled = !closed.IsCancellationRequested; }
            };
            table.Children.Add(calculate);
            table.Children.Add(resultText);
        }
    }

    private void ShowPokerReview()
    {
        var (window, content, _) = CreateDockPanel("Hand review", "", 710, 0xE81C, accent,
            backAction: ShowPokerLobby, backLabel: "Back to Poker", height: 565, expandable: true);
        var source = new ComboBox { ItemsSource = new[] { "Cash", "Tournament", "Practice" }, SelectedIndex = 0 };
        var hands = new ComboBox { DisplayMemberPath = nameof(PokerReviewItem.Label) };
        AutomationProperties.SetName(source, "Hand source");
        AutomationProperties.SetName(hands, "Completed hand");
        var body = new StackPanel();
        var navigation = new WrapPanel();
        var timer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher) { Interval = TimeSpan.FromMilliseconds(750) };
        var step = 0;
        content.Children.Add(Labeled("Mode", source));
        content.Children.Add(Labeled("Hand", hands));
        content.Children.Add(body);
        content.Children.Add(navigation);
        source.SelectionChanged += (_, _) => Reload();
        hands.SelectionChanged += (_, _) => { timer.Stop(); step = 0; Render(); };
        timer.Tick += (_, _) => Next();
        window.IsVisibleChanged += (_, _) => { if (!window.IsVisible) timer.Stop(); };
        window.Closed += (_, _) => timer.Stop();
        Reload();
        window.Show();

        void Reload()
        {
            var played = source.SelectedIndex switch
            {
                1 => DockPokerTournament.Load().Replays ?? [],
                2 => DockPokerPractice.Load().History,
                _ => DockCasino.Load().History
            };
            var rows = played.Where(entry => entry.Game == CasinoGame.Holdem).Take(80)
                .Select(entry => new PokerReviewItem($"{entry.At.LocalDateTime:MMM d · HH:mm} · " +
                    $"{(entry.Rival is { } rival ? DockCasino.Opponent(rival).Name : "Hold’em")} · {entry.Returned - entry.Stake:+#,0.##;-#,0.##;0}",
                    entry)).ToArray();
            hands.ItemsSource = rows;
            hands.SelectedIndex = rows.Length == 0 ? -1 : 0;
            Render();
        }

        void Next()
        {
            if (hands.SelectedItem is not PokerReviewItem { Entry.Replay: { } replay })
                return;
            step = Math.Min(step + 1, replay.Steps.Length - 1);
            if (step == replay.Steps.Length - 1)
                timer.Stop();
            Render();
        }

        void Render()
        {
            body.Children.Clear();
            navigation.Children.Clear();
            if (hands.SelectedItem is not PokerReviewItem row)
            {
                body.Children.Add(Text("No completed hands in this mode yet.", 12, AssistantSecondary));
                return;
            }
            var entry = row.Entry;
            if (entry.Replay is not { Steps.Length: > 0 } replay)
            {
                foreach (var line in entry.Moves ?? [entry.Detail])
                    AddLine(body, line, 12, false);
                return;
            }
            var frame = replay.Steps[step];
            var last = step == replay.Steps.Length - 1;
            var layout = new StackPanel();
            AddLine(layout, $"{DockCasino.HoldemStreet(frame.BoardCount)} · {frame.Pot:#,0} in pot · {step + 1} of {replay.Steps.Length}", 14, true);
            var cards = new WrapPanel { Margin = new Thickness(0, 7, 0, 7) };
            foreach (var card in replay.Player)
                cards.Children.Add(PokerCard(card));
            for (var index = 0; index < 5; index++)
                cards.Children.Add(PokerCard(index < frame.BoardCount ? replay.Board[index] : null, index == 0));
            layout.Children.Add(cards);
            if (last && replay.RevealedRival is { } shown)
                AddLine(layout, $"Rival showdown · {DockCasino.Hand(shown)}", 12, false);
            AddLine(layout, frame.Detail, 14, true);
            body.Children.Add(Card(layout));
            AddControl("Restart", () => { timer.Stop(); step = 0; Render(); }, step > 0);
            AddControl("Previous action", () => { timer.Stop(); step--; Render(); }, step > 0);
            AddControl("Next action", () => { timer.Stop(); Next(); }, !last);
            AddControl(timer.IsEnabled ? "Pause replay" : "Play replay", () =>
            {
                if (timer.IsEnabled) timer.Stop();
                else
                {
                    if (last) step = 0;
                    timer.Start();
                }
                Render();
            }, replay.Steps.Length > 1);
        }

        void AddControl(string label, Action action, bool enabled)
        {
            var button = ActionButton(label, action);
            button.Margin = new Thickness(0, 5, 5, 0);
            button.IsEnabled = enabled;
            navigation.Children.Add(button);
        }

        void AddLine(Panel panel, string value, int size, bool strong)
        {
            var label = Text(value, size, strong ? AssistantInk : AssistantSecondary, strong);
            label.TextWrapping = TextWrapping.Wrap;
            label.Margin = new Thickness(0, 2, 0, 2);
            panel.Children.Add(label);
        }
    }

    private sealed record PokerReviewItem(string Label, CasinoEntry Entry);
}
