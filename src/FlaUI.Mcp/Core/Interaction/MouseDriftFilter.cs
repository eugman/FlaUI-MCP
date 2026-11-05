namespace FlaUI.Mcp.Core;

/// <summary>
/// Tolerates a physical mouse drifting a few pixels (a nudge, a desk knock, an optical sensor's jitter) without
/// counting it as the user taking the machine back. Deliberately narrow: only a physical WM_MOUSEMOVE may be
/// tolerated, and only while the pointer stays within <see cref="MaxRadius"/> px of the drift's anchor, however many
/// events the motion is reported as. Keys, button downs and ups, and wheels always count, and so does any move past
/// the radius, at once.
/// </summary>
/// <remarks>
/// The anchor is the point a drift starts from: the last move point seen from either source when a physical move
/// follows <see cref="WindowMs"/> of physical quiet. Tolerated moves never move the anchor, so slow creep cannot add
/// up past the radius within one drift; stop-and-go creep gains at most the radius per quiet second. A hand resting on
/// the mouse and jittering within the radius is tolerated for as long as it stays there. The hook sees the agent's
/// injected events too: injected moves move the reference point, and any injected mouse event drops the anchor
/// (without counting), so they cannot mask a real move. Nothing is tolerated within <see cref="InjectedQuietMs"/> of
/// an injected mouse event, or before any reference point exists. Runs on the hook thread only; it keeps no
/// coordinates beyond the last move point and the anchor.
/// </remarks>
internal sealed class MouseDriftFilter
{
    internal const long WindowMs = 1_000;
    internal const double MaxRadius = 10;
    internal const long InjectedQuietMs = 500;

    private (int X, int Y)? reference;
    private (int X, int Y)? anchor;
    private long? lastPhysicalMove;
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
            anchor = null;
            return false;
        }

        return !isMove || !Tolerates(previous, x, y, now);
    }

    private bool Tolerates((int X, int Y)? previous, int x, int y, long now)
    {
        var quiet = lastPhysicalMove is not { } last || now - last >= WindowMs;
        lastPhysicalMove = now;
        if (anchor is null || quiet)
        {
            anchor = previous;
        }

        // Without a reference point the move's size is unknown, so it is too large to tolerate.
        if (
            AfterInjected(now)
            || anchor is not { } from
            || Square(x - from.X) + Square(y - from.Y) > MaxRadius * MaxRadius
        )
        {
            return false;
        }

        Interlocked.Increment(ref tolerated);
        return true;
    }

    private bool AfterInjected(long now) => lastInjected is { } injected && now - injected < InjectedQuietMs;

    private static double Square(int value) => (double)value * value;
}
