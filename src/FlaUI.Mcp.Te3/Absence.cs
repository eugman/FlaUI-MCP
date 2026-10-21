using System.Diagnostics;

/// <summary>Waits for a UI target to be gone and stay gone; a failed read never counts as absence.</summary>
internal static class Absence
{
    private const int PollMs = 100;

    /// <summary>
    /// Returns once <paramref name="present"/> has read false for <paramref name="settleMs"/>; a read that sees it again,
    /// or cannot read it, restarts that wait. Throws TimeoutException after <paramref name="timeoutMs"/>.
    /// </summary>
    /// <param name="elapsed">The clock, real when null; tests pass a fake one with <paramref name="delay"/>.</param>
    /// <param name="delay">The pause between reads, Task.Delay when null.</param>
    public static async Task WaitFor(
        Func<Task<bool>> present,
        int timeoutMs = 15000,
        int settleMs = 500,
        Func<TimeSpan>? elapsed = null,
        Func<int, Task>? delay = null
    )
    {
        var clock = elapsed ?? Stopwatch.StartNew().ElapsedFrom();
        var pause = delay ?? (ms => Task.Delay(ms));
        var start = clock();
        TimeSpan? absentSince = null;
        Exception? lastUnavailable = null;
        while ((clock() - start).TotalMilliseconds < timeoutMs)
        {
            try
            {
                absentSince = await present() ? null : absentSince ?? clock();
                if (absentSince is { } since && (clock() - since).TotalMilliseconds >= settleMs)
                {
                    return;
                }
            }
            catch (Exception error)
                when (error is FlaUI.Core.Exceptions.ElementNotAvailableException
                    || error is System.Runtime.InteropServices.COMException { HResult: unchecked((int)0x80040201) }
                )
            {
                // A window can disappear between enumeration and a property read.
                // Re-observe from fresh roots; a failed read never proves absence.
                absentSince = null;
                lastUnavailable = error;
            }

            await pause(PollMs);
        }

        throw new TimeoutException("Target did not remain absent for the settle period", lastUnavailable);
    }

    private static Func<TimeSpan> ElapsedFrom(this Stopwatch watch) => () => watch.Elapsed;
}
