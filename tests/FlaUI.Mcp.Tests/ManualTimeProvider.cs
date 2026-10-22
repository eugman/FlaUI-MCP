namespace FlaUI.Mcp.Tests;

/// <summary>A clock that moves only when a test calls <see cref="Advance"/>; its timers fire then.</summary>
internal sealed class ManualTimeProvider : TimeProvider
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

    /// <summary>Move the clock and fire every timer that came due.</summary>
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
            timer.Fire();
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
