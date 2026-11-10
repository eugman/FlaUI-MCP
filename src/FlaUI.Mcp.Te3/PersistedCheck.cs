using System.Diagnostics;

/// <summary>
/// Reads of the persisted model after TE3's Save. Save is Ctrl+S with no read-back, so the write can land after the
/// first read: a check that expects a change is retried until it passes, and a check that expects none keeps reading
/// for a settle period before it believes what it read. On an injected clock, with a poll count that backstops it
/// (as SettlePoll has), so a still clock still ends the wait.
/// </summary>
internal static class PersistedCheck
{
    internal const int PollMs = 500;
    internal const int TimeoutMs = 10000;
    internal const int SettleMs = 3000;

    /// <summary>
    /// Runs <paramref name="check"/> until it passes. A failed TE CLI call or a mismatch (InvalidOperationException)
    /// is retried for <see cref="TimeoutMs"/>; after that the last failure propagates. Anything else propagates at once.
    /// </summary>
    public static async Task Passes(Func<Task> check, Func<TimeSpan>? elapsed = null, Func<int, Task>? delay = null)
    {
        var clock = elapsed ?? Elapsed(Stopwatch.StartNew());
        var pause = delay ?? (ms => Task.Delay(ms));
        var start = clock();
        for (var polls = TimeoutMs / PollMs; ; polls--)
        {
            try
            {
                await check();
                return;
            }
            catch (InvalidOperationException) when (polls > 0 && (clock() - start).TotalMilliseconds < TimeoutMs)
            {
                // The save may not have landed yet; read again.
            }

            await pause(PollMs);
        }
    }

    /// <summary>
    /// Reads until a value other than <paramref name="expected"/> appears or <see cref="SettleMs"/> has passed, and
    /// returns the last read: a change that lands late is seen, not taken for "unchanged".
    /// </summary>
    public static async Task<T> StaysAt<T>(
        T expected,
        Func<Task<T>> read,
        Func<TimeSpan>? elapsed = null,
        Func<int, Task>? delay = null
    )
    {
        var clock = elapsed ?? Elapsed(Stopwatch.StartNew());
        var pause = delay ?? (ms => Task.Delay(ms));
        var start = clock();
        var polls = SettleMs / PollMs;
        var now = await read();
        while (Equals(now, expected) && (clock() - start).TotalMilliseconds < SettleMs && polls-- > 0)
        {
            await pause(PollMs);
            now = await read();
        }

        return now;
    }

    private static Func<TimeSpan> Elapsed(Stopwatch watch) => () => watch.Elapsed;
}
