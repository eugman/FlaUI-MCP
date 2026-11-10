using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>The model checks after TE3's Save poll on a fake clock: nothing here sleeps or starts the TE CLI.</summary>
public sealed class PersistedCheckTests
{
    private readonly FakeClock clock = new();
    private int reads;

    private Task Passes(Func<Task> check) => PersistedCheck.Passes(check, clock.Elapsed, clock.Delay);

    [Fact]
    public async Task ACheckThatPassesIsReadOnce()
    {
        await Passes(() =>
        {
            reads++;
            return Task.CompletedTask;
        });

        Assert.Equal(1, reads);
        Assert.Equal(TimeSpan.Zero, clock.Now);
    }

    [Fact]
    public async Task ACheckIsRetriedUntilTheSaveHasLanded()
    {
        await Passes(() =>
            ++reads < 4 ? throw new InvalidOperationException("TE CLI failed (1): old value") : Task.CompletedTask
        );

        Assert.Equal(4, reads);
        Assert.Equal(3 * PersistedCheck.PollMs, clock.Now.TotalMilliseconds);
    }

    [Fact]
    public async Task ACheckThatNeverPassesFailsWithItsOwnErrorAtTheDeadline()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Passes(() => throw new InvalidOperationException("Query mismatch " + ++reads))
        );

        Assert.Equal(PersistedCheck.TimeoutMs, clock.Now.TotalMilliseconds);
        Assert.Equal("Query mismatch " + reads, error.Message);
    }

    [Fact]
    public async Task AClockThatDoesNotMoveStillEndsBothWaits()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PersistedCheck.Passes(
                () => throw new InvalidOperationException("mismatch " + ++reads),
                () => TimeSpan.Zero,
                _ => Task.CompletedTask
            )
        );
        Assert.Equal(PersistedCheck.TimeoutMs / PersistedCheck.PollMs + 1, reads);

        reads = 0;
        await PersistedCheck.StaysAt("same", Read("same"), () => TimeSpan.Zero, _ => Task.CompletedTask);
        Assert.Equal(PersistedCheck.SettleMs / PersistedCheck.PollMs + 1, reads);
    }

    [Fact]
    public async Task AnErrorThatIsNotAMismatchIsNotRetried()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Passes(() => throw new ArgumentException("Unsupported assertion " + ++reads))
        );

        Assert.Equal(1, reads);
    }

    private Func<Task<string>> Read(params string[] values) =>
        () => Task.FromResult(values[Math.Min(reads++, values.Length - 1)]);

    [Fact]
    public async Task AChangeThatLandsAfterTheFirstReadIsSeen()
    {
        var after = await PersistedCheck.StaysAt(
            "before",
            Read("before", "before", "edited"),
            clock.Elapsed,
            clock.Delay
        );

        Assert.Equal("edited", after);
        Assert.Equal(3, reads);
    }

    [Fact]
    public async Task UnchangedIsBelievedOnlyAfterTheSettlePeriod()
    {
        var after = await PersistedCheck.StaysAt("before", Read("before"), clock.Elapsed, clock.Delay);

        Assert.Equal("before", after);
        Assert.Equal(PersistedCheck.SettleMs, clock.Now.TotalMilliseconds);
    }
}
