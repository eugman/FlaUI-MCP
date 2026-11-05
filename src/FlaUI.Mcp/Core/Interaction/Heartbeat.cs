namespace FlaUI.Mcp.Core;

/// <summary>
/// Whether a thread that should beat regularly still does. One gap longer than StaleAfterMs poisons it for good:
/// Windows removes a low-level hook whose thread misses LowLevelHooksTimeout (about a second) without telling it, so
/// a later beat cannot prove the hooks came back. A new observer, started by the next batch, gets a new heartbeat.
/// </summary>
/// <remarks>
/// The latch keeps what it saw (<see cref="Fault"/>): the gap, the gap the next beat measured, the GC pause during the
/// gap, and whether Windows recorded any input in it. They explain a revocation; they never undo one.
/// </remarks>
/// <param name="gcPauseSource">The process's total GC pause so far; GC.GetTotalPauseDuration by default.</param>
/// <param name="lastInputSource">The low 32 bits of the tick of the last input Windows saw, null when unreadable.</param>
internal sealed class Heartbeat(long start, Func<TimeSpan>? gcPauseSource = null, Func<uint?>? lastInputSource = null)
{
    internal const int StaleAfterMs = 1_000;
    private const long NoBeat = long.MinValue;
    private readonly Func<TimeSpan> gcPause = gcPauseSource ?? GC.GetTotalPauseDuration;
    private readonly Func<uint?> lastInputTick = lastInputSource ?? (() => null);
    private long last = start;
    private long gcAtLast = (gcPauseSource ?? GC.GetTotalPauseDuration)().Ticks;
    private int poisoned;
    private ObserverFault? latched;
    private long nextBeatGap = NoBeat;

    public void Beat(long now)
    {
        var gap = now - Interlocked.Read(ref last);
        Check(now);
        if (Volatile.Read(ref poisoned) != 0)
        {
            Interlocked.CompareExchange(ref nextBeatGap, gap, NoBeat);
        }

        Interlocked.Exchange(ref last, now);
        Interlocked.Exchange(ref gcAtLast, gcPause().Ticks);
    }

    public bool Alive(long now)
    {
        Check(now);
        return Volatile.Read(ref poisoned) == 0;
    }

    /// <summary>What the latch saw, or null while alive.</summary>
    public ObserverFault? Fault
    {
        get
        {
            var fault = Volatile.Read(ref latched);
            var next = Interlocked.Read(ref nextBeatGap);
            return fault == null || next == NoBeat ? fault : fault with { GapAtNextBeatMs = next };
        }
    }

    private void Check(long now)
    {
        var since = Interlocked.Read(ref last);
        var gap = now - since;
        if (gap <= StaleAfterMs || Interlocked.CompareExchange(ref poisoned, 1, 0) != 0)
        {
            return;
        }

        var pause = TimeSpan.FromTicks(gcPause().Ticks - Interlocked.Read(ref gcAtLast));
        Volatile.Write(
            ref latched,
            new ObserverFault(
                ObserverFault.HeartbeatGap,
                gap,
                GcPauseMs: (long)pause.TotalMilliseconds,
                InputDuringGap: InputDuring(since, gap)
            )
        );
    }

    // GetLastInputInfo's tick is 32 bits wide: compare in that width, so a wrap between the two reads is harmless.
    private bool? InputDuring(long since, long gap)
    {
        if (lastInputTick() is not { } input)
        {
            return null;
        }

        var afterBeat = unchecked(input - (uint)since);
        return afterBeat > 0 && afterBeat <= gap;
    }
}
