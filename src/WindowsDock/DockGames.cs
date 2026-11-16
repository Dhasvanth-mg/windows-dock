namespace WindowsDock;

// Rules for the two small games on the Play tab. No UI and no timers in here, so they can be tested directly.

/// <summary>
/// Snake on a field with no walls: leaving one edge brings you back on the opposite one. It ends when the head runs
/// into the body, or when there is nowhere left to put food.
/// </summary>
internal sealed class SnakeGame
{
    internal const int Columns = 30;
    internal const int Rows = 11;
    private readonly LinkedList<(int X, int Y)> body = new();
    private readonly HashSet<(int X, int Y)> occupied = [];
    private readonly Random random;
    private (int X, int Y) heading = (1, 0);
    private (int X, int Y) queued = (1, 0);

    internal SnakeGame(int? seed = null)
    {
        random = seed is { } value ? new Random(value) : new Random();
        for (var x = Columns / 2 - 2; x <= Columns / 2; x++)
        {
            body.AddFirst((x, Rows / 2));
            occupied.Add((x, Rows / 2));
        }
        PlaceFood();
    }

    /// <summary>Head first.</summary>
    internal IEnumerable<(int X, int Y)> Body => body;
    internal (int X, int Y) Head => body.First!.Value;
    internal (int X, int Y) Heading => heading;
    internal (int X, int Y) Food { get; private set; }
    internal int Score { get; private set; }
    internal bool Over { get; private set; }

    /// <summary>Milliseconds between steps. It gets quicker as the snake grows, down to a floor.</summary>
    internal int Interval => Math.Max(70, 150 - Score * 3);

    /// <summary>Asks for a new direction on the next step. A reversal or a diagonal is ignored.</summary>
    internal void Turn(int x, int y)
    {
        if (Math.Abs(x) + Math.Abs(y) != 1 || (x, y) == (-heading.X, -heading.Y))
            return;
        queued = (x, y);
    }

    /// <summary>Moves one cell. Returns false once the game is over.</summary>
    internal bool Step()
    {
        if (Over)
            return false;
        heading = queued;
        var next = ((Head.X + heading.X + Columns) % Columns, (Head.Y + heading.Y + Rows) % Rows);
        var eats = next == Food;
        if (!eats)
        {
            // The tail moves out of the way first, so chasing your own tail is allowed.
            occupied.Remove(body.Last!.Value);
            body.RemoveLast();
        }
        if (occupied.Contains(next))
        {
            Over = true;
            return false;
        }
        body.AddFirst(next);
        occupied.Add(next);
        if (eats)
        {
            Score++;
            PlaceFood();
        }
        return !Over;
    }

    private void PlaceFood()
    {
        var free = Columns * Rows - occupied.Count;
        if (free == 0)
        {
            Over = true;
            return;
        }
        var pick = random.Next(free);
        for (var y = 0; y < Rows; y++)
            for (var x = 0; x < Columns; x++)
                if (!occupied.Contains((x, y)) && pick-- == 0)
                {
                    Food = (x, y);
                    return;
                }
    }
}

internal enum CopycatStep { Right, RoundComplete, Wrong }

/// <summary>
/// A memory game: a sequence over four players grows by one each round and has to be repeated in order.
/// </summary>
internal sealed class CopycatGame
{
    internal const int Players = 4;
    private readonly List<int> sequence = [];
    private readonly Random random;
    private int position;

    internal CopycatGame(int? seed = null) => random = seed is { } value ? new Random(value) : new Random();

    internal IReadOnlyList<int> Sequence => sequence;
    internal bool Over { get; private set; }

    /// <summary>Rounds repeated correctly from start to finish.</summary>
    internal int Score { get; private set; }

    /// <summary>Starts the next round by adding one more step to remember.</summary>
    internal void Extend()
    {
        if (Over)
            return;
        sequence.Add(random.Next(Players));
        position = 0;
    }

    internal CopycatStep Press(int player)
    {
        if (Over || position >= sequence.Count)
            return CopycatStep.Wrong;
        if (sequence[position] != player)
        {
            Over = true;
            return CopycatStep.Wrong;
        }
        position++;
        if (position < sequence.Count)
            return CopycatStep.Right;
        Score++;
        return CopycatStep.RoundComplete;
    }
}

internal sealed record GameScores(int Snake, int Copycat);
