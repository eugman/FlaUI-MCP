namespace FlaUI.Mcp.Tests;

/// <summary>
/// The one wait the offline tests use for work on another thread. Timing is never asserted through it: the bound is a
/// generous failure limit, and a test that needs time to pass moves a <see cref="ManualTimeProvider"/> instead.
/// </summary>
internal static class Eventually
{
    /// <summary>How long a condition may take before the test fails; only ever reached when something is broken.</summary>
    public static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    /// <summary>Wait until <paramref name="condition"/> holds, and fail the test (never pass silently) when it does not.</summary>
    public static void WaitUntil(Func<bool> condition, string because = "the condition")
    {
        if (!SpinWait.SpinUntil(condition, Bound))
        {
            throw new Xunit.Sdk.XunitException($"Timed out after {Bound.TotalSeconds:0} s waiting for {because}.");
        }
    }

    /// <inheritdoc cref="WaitUntil(Func{bool}, string)"/>
    public static Task WaitUntilAsync(Func<bool> condition, string because = "the condition") =>
        Task.Run(() => WaitUntil(condition, because));

    /// <summary>The task's result once it finishes; fails the test when it does not finish within the bound.</summary>
    public static T Result<T>(Task<T> task)
    {
        if (!((IAsyncResult)task).AsyncWaitHandle.WaitOne(Bound))
        {
            throw new Xunit.Sdk.XunitException($"The call did not return within {Bound.TotalSeconds:0} s.");
        }

        return task.GetAwaiter().GetResult();
    }

    /// <summary>Run a blocking call on another thread and return its result; fails the test when it never returns.</summary>
    public static T Returns<T>(Func<T> call) => Result(Task.Run(call));

    /// <summary>
    /// Run a call that blocks until <paramref name="clock"/> passes a deadline it computes itself: keep moving the clock
    /// by <paramref name="step"/> until the call returns, so the deadline passes whenever the call took it.
    /// </summary>
    public static T PastDeadline<T>(ManualTimeProvider clock, TimeSpan step, Func<T> call)
    {
        var task = Task.Run(call);
        WaitUntil(
            () =>
            {
                if (!task.IsCompleted)
                {
                    clock.Advance(step);
                }

                return ((IAsyncResult)task).AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(20));
            },
            "the call to pass its deadline"
        );
        return task.GetAwaiter().GetResult();
    }
}
