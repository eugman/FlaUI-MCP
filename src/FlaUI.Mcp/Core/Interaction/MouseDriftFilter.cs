namespace FlaUI.Mcp.Core;

/// <summary>
/// Tolerates a physical mouse drifting a few pixels (a desk knock, an optical sensor's jitter) without counting it as
/// the user taking the machine back. Deliberately narrow: only a physical WM_MOUSEMOVE may be tolerated, and only
/// while, within a sliding second, there are at most <see cref="MaxMoves"/> physical moves that together travel at
/// most <see cref="MaxDistance"/> px. Keys, button downs and ups, and wheels always count, and so does any move
/// beyond those limits, at once.
/// </summary>
/// <remarks>
/// Distance is measured from the last move point seen from either source: the hook sees the agent's injected moves
/// too, so they move the reference point (without counting) and cannot mask a real move. Nothing is tolerated within
/// <see cref="InjectedQuietMs"/> of an injected mouse event, or before any reference point exists. Runs on the hook
/// thread only; it keeps no coordinates beyond the last move point.
/// </remarks>
internal sealed class MouseDriftFilter
{
    internal const long WindowMs = 1_000;
    internal const int MaxMoves = 4;
    internal const double MaxDistance = 8;
    internal const long InjectedQuietMs = 500;

    private readonly Queue<(long Time, double Distance)> moves = new();
    private (int X, int Y)? reference;
    private long? lastInjected;
    private long tolerated;

    /// <summary>How many physical moves were tolerated so far.</summary>
    public long Tolerated => Interlocked.Read(ref tolerated);

    /// <summary>Whether an event counts as the user's input. A physical key always does; an injected event never
    /// does, but an injected mouse event is tracked.</summary>
    /// <param name="x">The mouse event's screen point; ignored for a key.</param>
    /// <param name="now">Milliseconds on a monotonic clock.</param>
    public bool Counts(bool keyboard, nint message, bool physical, int x, int y, long now)
    {
        if (keyboard)
        {
            return physical;
        }

        var isMove = message == PhysicalInputObserver.MouseMove;
        var previous = reference;
        if (isMove)
        {
            reference = (x, y);
        }

        if (!physical)
        {
            lastInjected = now;
            return false;
        }

        return !isMove || !Tolerates(previous, x, y, now);
    }

    private bool Tolerates((int X, int Y)? previous, int x, int y, long now)
    {
        while (moves.Count > 0 && now - moves.Peek().Time >= WindowMs)
        {
            moves.Dequeue();
        }

        // Without a reference point the move's size is unknown, so it is too large to tolerate.
        var step = previous is { } from ? Math.Sqrt(Square(x - from.X) + Square(y - from.Y)) : MaxDistance + 1;
        moves.Enqueue((now, step));
        if (AfterInjected(now) || moves.Count > MaxMoves || moves.Sum(m => m.Distance) > MaxDistance)
        {
            return false;
        }

        Interlocked.Increment(ref tolerated);
        return true;
    }

    private bool AfterInjected(long now) => lastInjected is { } injected && now - injected < InjectedQuietMs;

    private static double Square(int value) => (double)value * value;
}
