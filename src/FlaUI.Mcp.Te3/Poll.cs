using System.Diagnostics;

/// <summary>Repeats a check until it passes or time runs out. UI state often settles a moment after input.</summary>
internal static class Poll
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(100);

    /// <returns>True when the condition passed; false when the timeout elapsed first.</returns>
    public static async Task<bool> Until(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            if (await condition())
            {
                return true;
            }

            if (clock.Elapsed >= timeout)
            {
                return false;
            }

            await Task.Delay(Interval);
        }
    }

    public static Task<bool> Until(Func<bool> condition, TimeSpan timeout) =>
        Until(() => Task.FromResult(condition()), timeout);
}
