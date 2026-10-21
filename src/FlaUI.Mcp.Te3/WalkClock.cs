using System.Diagnostics;

/// <summary>
/// Time limits for te3_navigate object. Core stops every tool call at 30 s (ToolRegistry, not configurable from this
/// host), so the walk stops itself on the time since the tool started, leaving room for a Down already in flight and
/// for clearing the search afterwards.
/// </summary>
internal static class WalkClock
{
    /// <summary>Total elapsed time after which no further Down is pressed; about 6 s stay for the final clear.</summary>
    public static readonly TimeSpan WalkStop = TimeSpan.FromSeconds(18);

    /// <summary>How long one Down may wait, reads included, for the selection to move.</summary>
    public static readonly TimeSpan MoveTimeout = TimeSpan.FromMilliseconds(1500);

    private const int PollMs = 60;

    /// <summary>How long a Left that collapses a group is given before the selection is read back.</summary>
    public const int CollapseSettleMs = 200;

    /// <summary>A clock started now.</summary>
    public static Func<TimeSpan> Start()
    {
        var watch = Stopwatch.StartNew();
        return () => watch.Elapsed;
    }

    /// <summary>Total elapsed time after which no further read of the selection is started (each takes about 1 s).</summary>
    public static readonly TimeSpan ReadStop = TimeSpan.FromSeconds(21);

    /// <summary>True while a read after the walk stopped still leaves time to clear the search inside Core's 30 s.</summary>
    public static Func<bool> CanRead(Func<TimeSpan> elapsed) => () => elapsed() < ReadStop;

    /// <summary>True once <paramref name="elapsed"/> (since the tool started) passes <see cref="WalkStop"/>.</summary>
    public static Func<bool> OutOfTime(Func<TimeSpan> elapsed) => () => elapsed() > WalkStop;

    /// <summary>
    /// Waits after a key press for the observation to change from <paramref name="before"/> and settle (a second read
    /// agrees). The wait is measured on <paramref name="elapsed"/>, so slow reads count. Null when nothing moved in time.
    /// </summary>
    public static async Task<T?> AwaitMove<T>(
        T before,
        Func<Task<T>> observe,
        Func<TimeSpan> elapsed,
        Func<int, Task> delay
    )
        where T : class
    {
        var start = elapsed();
        while (elapsed() - start < MoveTimeout)
        {
            await delay(PollMs);
            var now = await observe();
            if (now.Equals(before))
            {
                continue;
            }

            // Moved; take it once a second read agrees, so a half-updated status bar is not trusted.
            await delay(PollMs * 2);
            var settled = await observe();
            if (settled.Equals(now))
            {
                return settled;
            }
        }

        return null;
    }
}
