using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>The test clock fires due timers off the advancing thread, so a callback cannot deadlock against it.</summary>
public sealed class ManualTimeProviderTests
{
    [Fact]
    public void ACallbackBlockedOnTheAdvancingTestDoesNotHoldUpAdvance()
    {
        // PBI hung when ToolRegistry's timeout continuation ran inline on the test thread that moved the clock.
        var clock = new ManualTimeProvider();
        using var release = new ManualResetEventSlim();
        using var fired = new ManualResetEventSlim();
        using var timer = clock.CreateTimer(
            _ =>
            {
                release.Wait(Eventually.Bound);
                fired.Set();
            },
            null,
            TimeSpan.FromMilliseconds(10),
            Timeout.InfiniteTimeSpan
        );

        Assert.True(
            Eventually.Returns(() =>
            {
                clock.Advance(TimeSpan.FromMilliseconds(10));
                return true;
            })
        );
        Assert.False(fired.IsSet);

        release.Set();
        clock.WaitForCallbacks();
        Assert.True(fired.IsSet);
    }

    [Fact]
    public void WaitForCallbacksReturnsOnceEveryDueCallbackHasRun()
    {
        var clock = new ManualTimeProvider();
        var runs = 0;
        using var first = clock.CreateTimer(
            _ => Interlocked.Increment(ref runs),
            null,
            TimeSpan.FromMilliseconds(5),
            Timeout.InfiniteTimeSpan
        );
        using var second = clock.CreateTimer(
            _ => Interlocked.Increment(ref runs),
            null,
            TimeSpan.FromMilliseconds(9),
            Timeout.InfiniteTimeSpan
        );

        clock.Advance(TimeSpan.FromMilliseconds(10));
        clock.WaitForCallbacks();

        Assert.Equal(2, runs);
        Assert.Equal(0, clock.PendingTimers);
    }
}
