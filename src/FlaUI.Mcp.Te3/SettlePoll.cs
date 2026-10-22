using System.Diagnostics;

/// <summary>
/// Reads until a popup or control is ready instead of one read after a fixed pause: on the injected clock, and with
/// a poll count that backstops it (as Core's ClickTool.Settled does), so a still clock still ends the wait. Returns
/// the last read, ready or not; the caller judges it.
/// </summary>
internal static class SettlePoll
{
    internal const int PollMs = 100;
    internal const int TimeoutMs = 2000;

    public static async Task<T> Until<T>(
        Func<Task<T>> read,
        Func<T, bool> ready,
        Func<TimeSpan>? elapsed = null,
        Func<int, Task>? delay = null
    )
    {
        var clock = elapsed ?? Elapsed(Stopwatch.StartNew());
        var pause = delay ?? (ms => Task.Delay(ms));
        var start = clock();
        var polls = TimeoutMs / PollMs + 1;
        var now = await read();
        while (!ready(now) && (clock() - start).TotalMilliseconds < TimeoutMs && polls-- > 0)
        {
            await pause(PollMs);
            now = await read();
        }

        return now;
    }

    private static Func<TimeSpan> Elapsed(Stopwatch watch) => () => watch.Elapsed;
}
