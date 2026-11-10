using System.Runtime.InteropServices;

namespace FlaUI.Mcp.Core;

/// <summary>
/// What the low-level hooks record, apart from the hooks themselves, so it can be driven with a fabricated hook
/// struct: which events are the user's own, how many, and the last one's kind. Written on the hook thread and read
/// from any; it holds primitives only, never a key or text, and of coordinates only where the last button went down.
/// </summary>
internal sealed class InputTally
{
    // KBDLLHOOKSTRUCT: vkCode, scanCode, flags. MSLLHOOKSTRUCT: POINT (x, y), mouseData, flags.
    private const int KeyboardFlagsOffset = 8;
    private const int MouseFlagsOffset = 12;
    private const int PointXOffset = 0;
    private const int PointYOffset = 4;

    private readonly MouseDriftFilter drift = new();
    private long sequence;
    private long lastEventSequence;
    private long lastEventUtcTicks;
    private int lastEventKind;
    private int lastEventVersion;
    private long keys;
    private long buttonDowns;
    private long lastButtonDown = long.MinValue;

    /// <summary>How many of the user's events were counted so far.</summary>
    public long Sequence => Interlocked.Read(ref sequence);

    /// <summary>Physical mouse moves the drift filter tolerated (not counted) so far.</summary>
    public long ToleratedMoves => drift.Tolerated;

    public InputEventInfo? LastEvent
    {
        get
        {
            // The hook writes primitives only. A version check prevents a torn diagnostic snapshot.
            var before = Volatile.Read(ref lastEventVersion);
            if ((before & 1) != 0)
            {
                return null;
            }

            var eventSequence = Volatile.Read(ref lastEventSequence);
            var ticks = Volatile.Read(ref lastEventUtcTicks);
            var kind = Volatile.Read(ref lastEventKind);
            if (before != Volatile.Read(ref lastEventVersion) || eventSequence == 0 || ticks == 0)
            {
                return null;
            }

            return new InputEventInfo(
                eventSequence,
                new DateTimeOffset(new DateTime(ticks, DateTimeKind.Utc)),
                kind == PhysicalInputObserver.KeyboardKind ? "keyboard" : "mouse",
                PhysicalInputObserver.MouseKindOf(kind)
            );
        }
    }

    public InputCounts Counts
    {
        get
        {
            var point = Interlocked.Read(ref lastButtonDown);
            return new(
                Interlocked.Read(ref keys),
                Interlocked.Read(ref buttonDowns),
                point == long.MinValue ? null : new System.Drawing.Point((int)(point >> 32), (int)point)
            );
        }
    }

    /// <summary>
    /// One hook event. <paramref name="data"/> points at the hook's KBDLLHOOKSTRUCT or MSLLHOOKSTRUCT;
    /// <paramref name="now"/> is milliseconds on a monotonic clock.
    /// </summary>
    public void Observe(bool isKeyboard, nint message, nint data, long now)
    {
        var flags = unchecked((uint)Marshal.ReadInt32(data, isKeyboard ? KeyboardFlagsOffset : MouseFlagsOffset));
        var physical = PhysicalInputObserver.IsPhysical(isKeyboard, flags);
        // Every mouse event, injected too, goes through the drift filter, which keeps the last move point. A key's
        // struct has no position and is never read for one.
        var (x, y) = isKeyboard
            ? (0, 0)
            : (Marshal.ReadInt32(data, PointXOffset), Marshal.ReadInt32(data, PointYOffset));
        if (!drift.Counts(isKeyboard, message, physical, x, y, now))
        {
            return;
        }

        Record(isKeyboard, message);
        if (isKeyboard)
        {
            Interlocked.Increment(ref keys);
        }
        else if (PhysicalInputObserver.IsButtonDown(message))
        {
            Interlocked.Exchange(ref lastButtonDown, ((long)x << 32) | (uint)y);
            Interlocked.Increment(ref buttonDowns);
        }
    }

    private void Record(bool isKeyboard, nint message)
    {
        Interlocked.Increment(ref lastEventVersion); // writer active
        var next = Interlocked.Increment(ref sequence);
        Volatile.Write(ref lastEventUtcTicks, DateTime.UtcNow.Ticks);
        Volatile.Write(ref lastEventKind, PhysicalInputObserver.KindOf(isKeyboard, message));
        Volatile.Write(ref lastEventSequence, next);
        Interlocked.Increment(ref lastEventVersion); // stable snapshot
    }
}
