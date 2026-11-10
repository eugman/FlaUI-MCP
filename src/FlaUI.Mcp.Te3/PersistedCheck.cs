using System.Diagnostics;

/// <summary>
/// Reads of the persisted model after TE3's Save. Save is Ctrl+S with no read-back, so the write can land after the
/// first read: a check that still reads the old value is read again until it passes, and a check that expects no
/// change is confirmed by one more read a moment later. On an injected clock, with a poll count that backstops it
/// (as SettlePoll has), so a still clock still ends the wait.
/// </summary>
internal static class PersistedCheck
{
    internal const int PollMs = 500;
    internal const int TimeoutMs = 10000;
    internal const int ConfirmMs = 1000;

    /// <summary>
    /// Runs <paramref name="check"/> until it passes. Only a failure <paramref name="notYet"/> recognises (the model
    /// was read and still holds another value) is read again, for <see cref="TimeoutMs"/>; after that it propagates.
    /// Anything else, such as a TE CLI call that failed or a result of the wrong shape, propagates at once.
    /// </summary>
    public static async Task Passes(
        Func<Task> check,
        Func<Exception, bool> notYet,
        Func<TimeSpan>? elapsed = null,
        Func<int, Task>? delay = null
    )
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
            catch (Exception error) when (polls > 0 && (clock() - start).TotalMilliseconds < TimeoutMs && notYet(error))
            {
                // The save may not have landed yet; read again.
            }

            await pause(PollMs);
        }
    }

    /// <summary>
    /// One read, and when it shows <paramref name="expected"/>, one more after <see cref="ConfirmMs"/>: a change
    /// that lands just after the first read is seen, not taken for "unchanged". Returns the last read.
    /// </summary>
    public static async Task<T> Confirmed<T>(T expected, Func<Task<T>> read, Func<int, Task>? delay = null)
    {
        var first = await read();
        if (!Equals(first, expected))
        {
            return first;
        }

        await (delay ?? (ms => Task.Delay(ms)))(ConfirmMs);
        return await read();
    }

    private static Func<TimeSpan> Elapsed(Stopwatch watch) => () => watch.Elapsed;
}
