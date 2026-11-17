using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace WindowsDock;

public partial class DashboardWindow
{
    private const string ScoresFile = "games.json";
    private int playGame;

    private UIElement PlayPage()
    {
        var scores = DockData.Read<GameScores>(ScoresFile) ?? new GameScores(0, 0);
        var page = FillPage(new DockPanel { LastChildFill = true });
        var bar = new DockPanel { Margin = new Thickness(0, -2, 0, 10) };
        var best = Text("", 12, AssistantSecondary);
        best.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(best, Dock.Right);
        bar.Children.Add(best);
        var applets = new WrapPanel { Children = { QuietButton("Poker rooms", ShowPokerLobby) }, Margin = new Thickness(0, 0, 18, 0),
            VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(applets, Dock.Right);
        bar.Children.Add(applets);
        var stage = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch, Focusable = false };
        bar.Children.Add(Segmented("Game", ["Token snake", "Copycat", "Casino"], playGame, index =>
        {
            playGame = index;
            Mount();
        }));
        DockPanel.SetDock(bar, Dock.Top);
        page.Children.Add(bar);
        page.Children.Add(stage);
        Mount();
        return page;

        void Mount()
        {
            best.Text = playGame switch
            {
                0 => BestLine(scores.Snake, "token", "tokens"),
                1 => BestLine(scores.Copycat, "round", "rounds"),
                _ => "Offline, fictional chips"
            };
            stage.Content = playGame switch { 0 => SnakeStage(Record), 1 => CopycatStage(Record), _ => CasinoStage() };
        }

        static string BestLine(int value, string one, string many) =>
            value == 0 ? "No best yet" : "Best: " + Plural(value, one, many);

        // Returns true when the score just set a new best.
        bool Record(int score)
        {
            var previous = playGame == 0 ? scores.Snake : scores.Copycat;
            if (score <= previous)
                return false;
            scores = playGame == 0 ? scores with { Snake = score } : scores with { Copycat = score };
            try { DockData.Save(ScoresFile, scores); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            best.Text = playGame == 0 ? BestLine(scores.Snake, "token", "tokens") : BestLine(scores.Copycat, "round", "rounds");
            return true;
        }
    }

    private FrameworkElement SnakeStage(Func<int, bool> record)
    {
        var game = new SnakeGame();
        var board = new SnakeBoard(game, AccentFill, AssistantSecondary, assistantInset);
        var caption = Text("Space or click to start. Arrows or WASD to steer. The edges wrap around.", 13, AssistantSecondary);
        caption.HorizontalAlignment = HorizontalAlignment.Center;
        caption.VerticalAlignment = VerticalAlignment.Top;
        caption.Margin = new Thickness(0, 46, 0, 0);
        caption.IsHitTestVisible = false;
        var score = Text("", 12, AssistantSecondary);
        score.HorizontalAlignment = HorizontalAlignment.Left;
        score.VerticalAlignment = VerticalAlignment.Top;
        score.Margin = new Thickness(12, 8, 0, 0);
        score.IsHitTestVisible = false;
        var timer = new DispatcherTimer(DispatcherPriority.Input);
        var stage = new Grid { Focusable = true, FocusVisualStyle = null, Background = Brushes.Transparent };
        AutomationProperties.SetName(stage, "Token snake. Space starts, arrow keys steer.");
        stage.Children.Add(board);
        stage.Children.Add(score);
        stage.Children.Add(caption);

        timer.Tick += (_, _) =>
        {
            var alive = game.Step();
            score.Text = Plural(game.Score, "token", "tokens");
            if (!alive)
            {
                timer.Stop();
                var beaten = record(game.Score);
                caption.Text = (beaten ? "New best: " : "Ate ") + Plural(game.Score, "token", "tokens") +
                    (beaten ? ". " : " before tripping over yourself. ") + "Space to go again.";
                caption.Visibility = Visibility.Visible;
            }
            else
                timer.Interval = TimeSpan.FromMilliseconds(game.Interval);
            board.InvalidateVisual();
        };
        stage.MouseLeftButtonDown += (_, e) =>
        {
            stage.Focus();
            if (!timer.IsEnabled)
                Toggle();
            e.Handled = true;
        };
        stage.KeyDown += (_, e) =>
        {
            var turn = e.Key switch
            {
                Key.Left or Key.A => (-1, 0),
                Key.Right or Key.D => (1, 0),
                Key.Up or Key.W => (0, -1),
                Key.Down or Key.S => (0, 1),
                _ => (0, 0)
            };
            if (turn != (0, 0))
            {
                game.Turn(turn.Item1, turn.Item2);
                if (!timer.IsEnabled && !game.Over)
                    Toggle();
                e.Handled = true;
            }
            else if (e.Key is Key.Space or Key.Enter)
            {
                Toggle();
                e.Handled = true;
            }
        };
        // Leaving the page or hiding the dock pauses the round; nothing keeps ticking in the background.
        stage.Unloaded += (_, _) => timer.Stop();
        stage.IsVisibleChanged += (_, _) =>
        {
            if (!stage.IsVisible && timer.IsEnabled)
                Pause();
        };
        stage.Loaded += (_, _) => stage.Focus();
        return stage;

        void Toggle()
        {
            if (timer.IsEnabled)
            {
                Pause();
                return;
            }
            if (game.Over)
            {
                game = new SnakeGame();
                board.Game = game;
                score.Text = "";
            }
            caption.Visibility = Visibility.Collapsed;
            timer.Interval = TimeSpan.FromMilliseconds(game.Interval);
            timer.Start();
            board.InvalidateVisual();
        }

        void Pause()
        {
            timer.Stop();
            caption.Text = "Paused. Space to carry on.";
            caption.Visibility = Visibility.Visible;
        }
    }

    private FrameworkElement CopycatStage(Func<int, bool> record)
    {
        var game = new CopycatGame();
        var stage = new DockPanel { LastChildFill = true, Focusable = true, FocusVisualStyle = null, Background = Brushes.Transparent };
        AutomationProperties.SetName(stage, "Copycat. Repeat the order the assistants light up in. Keys 1 to 4 also work.");
        var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var start = PrimaryButton("Start");
        start.MinWidth = 72;
        DockPanel.SetDock(start, Dock.Right);
        footer.Children.Add(start);
        var line = Text("They light up in some order. You repeat it. Click them or press 1 to 4.", 13, AssistantSecondary);
        line.VerticalAlignment = VerticalAlignment.Center;
        line.TextWrapping = TextWrapping.Wrap;
        footer.Children.Add(line);
        DockPanel.SetDock(footer, Dock.Bottom);
        stage.Children.Add(footer);

        var listening = false;
        var turn = new CancellationTokenSource();
        var row = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1 };
        var tiles = new Border[CopycatGame.Players];
        var faces = new ScaleTransform[CopycatGame.Players];
        for (var index = 0; index < CopycatGame.Players; index++)
        {
            var player = index;
            var persona = AssistantPersonas.All[index];
            var portrait = PersonaPortrait(persona, 84);
            portrait.RenderTransformOrigin = new Point(.5, .6);
            portrait.RenderTransform = faces[index] = new ScaleTransform(1, 1);
            var name = Text(persona.Name, 13);
            name.HorizontalAlignment = HorizontalAlignment.Center;
            name.Margin = new Thickness(0, 4, 0, 0);
            var key = Text((index + 1).ToString(), 11, AssistantFaint);
            key.HorizontalAlignment = HorizontalAlignment.Center;
            var tile = new Border
            {
                CornerRadius = new CornerRadius(12), Margin = new Thickness(6, 0, 6, 0), Background = Brushes.Transparent,
                Child = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { portrait, name, key } }
            };
            tile.MouseLeftButtonDown += (_, e) =>
            {
                stage.Focus();
                Press(player);
                e.Handled = true;
            };
            AutomationProperties.SetName(tile, persona.Name);
            tiles[index] = tile;
            row.Children.Add(tile);
        }
        stage.Children.Add(row);

        start.Click += (_, _) =>
        {
            stage.Focus();
            game = new CopycatGame();
            _ = NextRoundAsync();
        };
        stage.KeyDown += (_, e) =>
        {
            var player = e.Key switch
            {
                >= Key.D1 and <= Key.D4 => e.Key - Key.D1,
                >= Key.NumPad1 and <= Key.NumPad4 => e.Key - Key.NumPad1,
                _ => -1
            };
            if (player >= 0)
            {
                Press(player);
                e.Handled = true;
            }
            else if (e.Key is Key.Space or Key.Enter && start.IsEnabled && e.OriginalSource == stage)
            {
                game = new CopycatGame();
                _ = NextRoundAsync();
                e.Handled = true;
            }
        };
        stage.Unloaded += (_, _) => turn.Cancel();
        return stage;

        void Light(int player, bool on)
        {
            tiles[player].Background = on ? PersonaWash(AssistantPersonas.All[player]) : Brushes.Transparent;
            faces[player].ScaleX = faces[player].ScaleY = on ? 1.12 : 1;
        }

        async Task NextRoundAsync()
        {
            turn.Cancel();
            turn = new CancellationTokenSource();
            var token = turn.Token;
            listening = false;
            start.IsEnabled = false;
            game.Extend();
            line.Text = game.Sequence.Count == 1 ? "Watch closely." : $"Round {game.Sequence.Count}. Watch.";
            try
            {
                await Task.Delay(450, token);
                var hold = Math.Max(170, 400 - game.Sequence.Count * 14);
                foreach (var player in game.Sequence)
                {
                    Light(player, true);
                    await Task.Delay(hold, token);
                    Light(player, false);
                    await Task.Delay(130, token);
                }
                listening = true;
                line.Text = "Your turn.";
            }
            catch (OperationCanceledException)
            {
                for (var index = 0; index < tiles.Length; index++)
                    Light(index, false);
            }
        }

        async void Press(int player)
        {
            if (!listening)
                return;
            var token = turn.Token;
            var step = game.Press(player);
            Light(player, true);
            try { await Task.Delay(140, token); }
            catch (OperationCanceledException) { return; }
            finally { Light(player, false); }
            if (step == CopycatStep.Wrong)
            {
                listening = false;
                var beaten = record(game.Score);
                var who = AssistantPersonas.All[game.Sequence[Math.Min(game.Sequence.Count - 1, game.Score)]].Name;
                line.Text = (beaten ? "New best: " + Plural(game.Score, "round", "rounds") + ". " :
                    game.Score == 0 ? $"{who} is not impressed. " : $"{who} caught you after {Plural(game.Score, "round", "rounds")}. ") +
                    "Start again when you are ready.";
                start.Content = "Again";
                start.IsEnabled = true;
            }
            else if (step == CopycatStep.RoundComplete)
            {
                listening = false;
                line.Text = "Got it.";
                _ = NextRoundAsync();
            }
        }
    }

    private static SolidColorBrush PersonaWash(AssistantPersona persona)
    {
        var tint = persona.Color;
        var brush = new SolidColorBrush(Color.FromArgb(54, tint.R, tint.G, tint.B));
        brush.Freeze();
        return brush;
    }

    // Draws the whole field in one pass. A few hundred rectangles at most, and only when the game asks for a repaint.
    private sealed class SnakeBoard : FrameworkElement
    {
        private readonly Brush body;
        private readonly Brush food;
        private readonly Brush field;

        internal SnakeBoard(SnakeGame game, Brush body, Brush food, Brush field)
        {
            Game = game;
            this.body = body;
            this.food = food;
            this.field = field;
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        }

        internal SnakeGame Game { get; set; }

        protected override void OnRender(DrawingContext context)
        {
            var cell = Math.Floor(Math.Min((ActualWidth - 12) / SnakeGame.Columns, (ActualHeight - 12) / SnakeGame.Rows));
            if (cell < 4)
                return;
            var width = cell * SnakeGame.Columns;
            var height = cell * SnakeGame.Rows;
            var left = Math.Floor((ActualWidth - width) / 2);
            var top = Math.Floor((ActualHeight - height) / 2);
            context.DrawRoundedRectangle(field, null, new Rect(left - 6, top - 6, width + 12, height + 12), 10, 10);

            var crumb = Game.Food;
            context.DrawEllipse(food, null, new Point(left + (crumb.X + .5) * cell, top + (crumb.Y + .5) * cell), cell * .22, cell * .22);

            var head = true;
            var length = Game.Body.Count();
            var index = 0;
            foreach (var (x, y) in Game.Body)
            {
                var spot = new Rect(left + x * cell + 1, top + y * cell + 1, cell - 2, cell - 2);
                if (head)
                {
                    // The head is the coding-chat cat, so the snake is really a cat with a very long tail.
                    var face = AgentArt.Face(Game.Over ? AgentState.Failed : AgentState.Working);
                    var grown = new Rect(spot.X - cell * .18, spot.Y - cell * .02, cell * 1.36 - 2, cell * 1.02 - 2);
                    context.DrawImage(face, grown);
                    head = false;
                }
                else
                {
                    context.PushOpacity(1 - .55 * index / Math.Max(1, length));
                    context.DrawRoundedRectangle(body, null, spot, 4, 4);
                    context.Pop();
                }
                index++;
            }
        }
    }
}
