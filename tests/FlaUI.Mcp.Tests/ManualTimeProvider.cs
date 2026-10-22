namespace FlaUI.Mcp.Tests;

/// <summary>A clock that moves only when a test calls <see cref="Advance"/>; its timers fire then, on the thread pool.</summary>
internal sealed class ManualTimeProvider : TimeProvider, IManualClock
{
    private readonly object sync = new();
    private readonly List<ManualTimer> timers = [];
    private long ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp()
    {
        lock (sync)
        {
            return ticks;
        }
    }

    private int nowReads;

    /// <summary>How many times the clock was read for the time of day: a loop that checks a deadline reads it each turn.</summary>
    public int NowReads => Volatile.Read(ref nowReads);

    public override DateTimeOffset GetUtcNow()
    {
        Interlocked.Increment(ref nowReads);
        return new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(GetTimestamp());
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>How many timers are waiting to come due: a call that is waiting on this clock has made one.</summary>
    public int PendingTimers
    {
        get
        {
            lock (sync)
            {
                return timers.Count(t => t.DueAt != null);
            }
        }
    }

    private int running;

    /// <summary>Wait until every callback queued by an earlier <see cref="Advance"/> has run.</summary>
    public void WaitForCallbacks() =>
        Eventually.WaitUntil(() => Volatile.Read(ref running) == 0, "the clock's timer callbacks to run");

    /// <summary>
    /// Move the clock and queue every timer that came due to the thread pool, as a real timer does. Fired inline, a
    /// callback (a registry timeout's continuation, say) would run on the test thread that moved the clock, and
    /// deadlock when it waits on something that thread holds. Call <see cref="WaitForCallbacks"/> to see their effect.
    /// </summary>
    public void Advance(TimeSpan by)
    {
        List<ManualTimer> due;
        lock (sync)
        {
            ticks += by.Ticks;
            due = timers.Where(t => t.DueAt is { } at && at <= ticks).ToList();
            foreach (var timer in due)
            {
                timer.DueAt = null;
            }
        }

        foreach (var timer in due)
        {
            Interlocked.Increment(ref running);
            ThreadPool.UnsafeQueueUserWorkItem(
                _ =>
                {
                    try
                    {
                        timer.Fire();
                    }
                    finally
                    {
                        Interlocked.Decrement(ref running);
                    }
                },
                null
            );
        }
    }

    private sealed class ManualTimer(ManualTimeProvider clock, TimerCallback callback, object? state) : ITimer
    {
        public long? DueAt { get; set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (clock.sync)
            {
                clock.timers.Remove(this);
                DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : clock.ticks + dueTime.Ticks;
                clock.timers.Add(this);
            }

            return true;
        }

        public void Fire() => callback(state);

        public void Dispose()
        {
            lock (clock.sync)
            {
                clock.timers.Remove(this);
                DueAt = null;
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
