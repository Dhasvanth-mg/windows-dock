using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace WindowsDock;

public partial class DashboardWindow
{
    private void ShowPokerTournament() => ShowPokerTournament(false);

    private void ShowPokerTournament(bool fromPoker)
    {
        var (window, content, _) = CreateDockPanel("Hold’em tournament", "Offline · four entrants", 765, 0xE7FC, accent,
            backAction: fromPoker ? ShowPokerLobby : ShowCasino, backLabel: fromPoker ? "Back to Poker" : "Back to Casino",
            height: 625, expandable: true);
        var saved = DockPokerTournament.Load();
        var format = new ComboBox { ItemsSource = Enum.GetValues<PokerFormat>(), SelectedItem = saved.Current?.Format ?? PokerFormat.Quick };
        AutomationProperties.SetName(format, "Tournament format");
        var details = Text("", 12, AssistantSecondary);
        var status = Text("", 12, AssistantSecondary);
        status.TextWrapping = TextWrapping.Wrap;
        var setup = new StackPanel();
        var table = new StackPanel();
        var bracket = new StackPanel();
        var results = new StackPanel();
        var begin = ActionButton("Enter tournament");
        begin.HorizontalAlignment = HorizontalAlignment.Left;
        setup.Children.Add(Labeled("Structure", format));
        setup.Children.Add(details);
        setup.Children.Add(begin);
        content.Children.Add(setup);
        content.Children.Add(status);
        content.Children.Add(table);
        content.Children.Add(bracket);
        content.Children.Add(new Expander
        {
            Header = "Recent tournaments", Style = (Style)window.FindResource("AppletSection"), Content = results
        });
        begin.Click += (_, _) => Change(current => DockPokerTournament.Start(current, (PokerFormat)format.SelectedItem));
        format.SelectionChanged += (_, _) => Describe();
        Describe();
        Render();
        window.Show();

        void Describe()
        {
            var option = (PokerFormat)format.SelectedItem;
            details.Text = $"{DockPokerTournament.StartingStack(option)} chips per seat · blinds double every " +
                $"{DockPokerTournament.HandsPerLevel(option)} hands · separate tournament bankroll";
            details.TextWrapping = TextWrapping.Wrap;
        }

        void Change(Func<TournamentSave, TournamentSave> edit)
        {
            try
            {
                saved = DockPokerTournament.Update(edit);
                status.Text = "";
                Render();
            }
            catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
            {
                status.Text = error.Message;
            }
        }

        void Render()
        {
            table.Children.Clear();
            bracket.Children.Clear();
            results.Children.Clear();
            setup.Visibility = saved.Current is { Active: true } ? Visibility.Collapsed : Visibility.Visible;
            if (saved.Current is { } match)
            {
                var stage = match.Stage switch
                {
                    TournamentStage.Champion => "Tournament champion",
                    TournamentStage.Eliminated => "Eliminated",
                    TournamentStage.Withdrawn => "Withdrawn",
                    TournamentStage.Final => "Final",
                    _ => "Semifinal"
                };
                var name = Name(match.Opponent);
                var controls = new StackPanel { Children = { Text(stage, 19, AssistantInk, true) } };
                if (match.Active)
                    controls.Children.Add(Text($"You {match.PlayerChips:#,0}  ·  {name} {match.RivalChips:#,0}  ·  " +
                        $"big blind {DockPokerTournament.BigBlind(match.Format, match.TableHands)}", 12, AssistantSecondary));
                if (match.Hand is { Poker: { } poker } round)
                {
                    controls.Children.Add(Text($"{DockCasino.HoldemStreet(round.Point)} · {poker.Pot:#,0} in pot", 13, AssistantInk, true));
                    var hand = new StackPanel();
                    var actions = new WrapPanel();
                    PopulatePokerHand(round, poker, hand, actions, (move, amount) =>
                        Change(current => DockPokerTournament.Play(current, match.Id, poker.Id, poker.Turn, move, amount?.Invoke() ?? 0)));
                    controls.Children.Add(hand);
                    controls.Children.Add(actions);
                }
                else
                {
                    var result = Text(match.LastResult, 12, AssistantSecondary);
                    result.TextWrapping = TextWrapping.Wrap;
                    controls.Children.Add(result);
                    if (match.Active)
                    {
                        var deal = ActionButton("Deal tournament hand", () =>
                            Change(current => DockPokerTournament.Deal(current, match.Id, match.Hands)));
                        deal.HorizontalAlignment = HorizontalAlignment.Left;
                        deal.Margin = new Thickness(0, 7, 0, 0);
                        controls.Children.Add(deal);
                    }
                }
                var card = Card(controls);
                card.Margin = new Thickness(0, 5, 0, 10);
                table.Children.Add(card);
                bracket.Children.Add(SectionTitle("Bracket"));
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                var first = match.ReachedFinal ? "You advanced" :
                    match.Stage == TournamentStage.Eliminated ? $"{Name(match.FirstOpponent)} advanced" :
                    match.Stage == TournamentStage.Withdrawn ? "Withdrawn" : "In play";
                var finalists = match.ReachedFinal ? $"You vs {Name(match.FinalOpponent)}" :
                    match.Stage == TournamentStage.Eliminated ? $"{Name(match.FirstOpponent)} vs {Name(match.FinalOpponent)}" :
                    $"Semifinal winner vs {Name(match.FinalOpponent)}";
                var final = match.Stage == TournamentStage.Champion ? "You won" :
                    match.WinningRival is { } winner ? $"{Name(winner)} won" :
                    match.Stage == TournamentStage.Final ? "In play" :
                    match.Stage == TournamentStage.Withdrawn ? "Withdrawn" : "Awaiting result";
                var semifinals = new StackPanel { Margin = new Thickness(0, 0, 5, 0) };
                semifinals.Children.Add(MatchCard($"You vs {Name(match.FirstOpponent)}", first));
                semifinals.Children.Add(MatchCard($"{Name(match.OtherFirst)} vs {Name(match.OtherSecond)}",
                    $"{Name(match.FinalOpponent)} won in {match.OtherMatchHands} hands"));
                var finals = MatchCard(finalists, final);
                finals.VerticalAlignment = VerticalAlignment.Center;
                finals.Margin = new Thickness(5, 0, 0, 0);
                Grid.SetColumn(finals, 1);
                grid.Children.Add(semifinals);
                grid.Children.Add(finals);
                bracket.Children.Add(grid);
                if (match.Active)
                    bracket.Children.Add(QuietButton("Withdraw", () =>
                    {
                        if (MessageBox.Show(DialogOwner(window), "Withdraw from this tournament and end the current run?", "Hold’em tournament",
                            MessageBoxButton.YesNo, MessageBoxImage.None, MessageBoxResult.No) == MessageBoxResult.Yes)
                            Change(current => DockPokerTournament.Withdraw(current, match.Id));
                    }));
            }
            if (saved.Results.Length == 0)
                results.Children.Add(Text("No completed runs yet.", 12, AssistantSecondary));
            foreach (var result in saved.Results)
            {
                var finish = result.Finish == TournamentStage.Champion ? "Champion" :
                    result.Finish == TournamentStage.Eliminated ? result.ReachedFinal ? "Finalist" : "Semifinalist" : "Withdrawn";
                results.Children.Add(Text($"{result.Finished:MMM d} · {result.Format} · {finish} · {result.Hands} hands", 12, AssistantSecondary));
            }
        }

        Border MatchCard(string title, string result)
        {
            var caption = Text(title, 12, AssistantInk, true);
            caption.TextWrapping = TextWrapping.Wrap;
            var row = Card(new StackPanel { Children = { caption, Text(result, 11, AssistantSecondary) } });
            row.Margin = new Thickness(0, 0, 0, 4);
            return row;
        }

        static string Name(HoldemRival rival) => DockCasino.Opponent(rival).Name;
    }
}
