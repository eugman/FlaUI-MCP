using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>
/// The popup captures read the popup once after a fixed 500 ms; they now poll until it is ready, on the injected
/// clock and with a poll cap, so a slow popup is waited for and a still clock still ends the wait. Scripted reads.
/// </summary>
public sealed class SettlePollTests
{
    [Fact]
    public async Task APopupThatOpensLateIsWaitedForOnTheClock()
    {
        var clock = new FakeClock();
        var reads = 0;

        var seen = await SettlePoll.Until(
            () => Task.FromResult(++reads >= 4 ? "popup" : null),
            popup => popup != null,
            clock.Elapsed,
            clock.Delay
        );

        Assert.Equal("popup", seen);
        Assert.Equal(TimeSpan.FromMilliseconds(3 * SettlePoll.PollMs), clock.Now);
    }

    [Fact]
    public async Task APopupThatNeverOpensReturnsTheLastReadAfterTheTimeout()
    {
        var clock = new FakeClock();

        var seen = await SettlePoll.Until(
            () => Task.FromResult<string?>(null),
            p => p != null,
            clock.Elapsed,
            clock.Delay
        );

        Assert.Null(seen);
        Assert.InRange(clock.Now.TotalMilliseconds, SettlePoll.TimeoutMs, SettlePoll.TimeoutMs + SettlePoll.PollMs);
    }

    [Fact]
    public async Task AStillClockEndsAtThePollCap()
    {
        var reads = 0;

        await SettlePoll.Until(
            () => Task.FromResult<string?>(reads++ < 0 ? "x" : null),
            p => p != null,
            () => TimeSpan.Zero,
            _ => Task.CompletedTask
        );

        Assert.InRange(reads, 1, SettlePoll.TimeoutMs / SettlePoll.PollMs + 2);
    }
}
