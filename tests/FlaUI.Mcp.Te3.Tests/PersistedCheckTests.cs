using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>The model checks after TE3's Save poll on a fake clock: nothing here sleeps or starts the TE CLI.</summary>
public sealed class PersistedCheckTests
{
    private readonly FakeClock clock = new();
    private int reads;

    private static bool OldValue(Exception error) => error is QueryValueMismatchException;

    private Task Passes(Func<Task> check) => PersistedCheck.Passes(check, OldValue, clock.Elapsed, clock.Delay);

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
    public async Task AValueNotYetPersistedIsReadAgainUntilTheSaveHasLanded()
    {
        await Passes(() => ++reads < 4 ? throw new QueryValueMismatchException("old value") : Task.CompletedTask);

        Assert.Equal(4, reads);
        Assert.Equal(3 * PersistedCheck.PollMs, clock.Now.TotalMilliseconds);
    }

    [Fact]
    public async Task AValueThatNeverArrivesFailsWithItsOwnErrorAtTheDeadline()
    {
        var error = await Assert.ThrowsAsync<QueryValueMismatchException>(() =>
            Passes(() => throw new QueryValueMismatchException("Query mismatch " + ++reads))
        );

        Assert.Equal(PersistedCheck.TimeoutMs, clock.Now.TotalMilliseconds);
        Assert.Equal("Query mismatch " + reads, error.Message);
    }

    [Fact]
    public async Task AClockThatDoesNotMoveStillEndsTheWait()
    {
        await Assert.ThrowsAsync<QueryValueMismatchException>(() =>
            PersistedCheck.Passes(
                () => throw new QueryValueMismatchException("mismatch " + ++reads),
                OldValue,
                () => TimeSpan.Zero,
                _ => Task.CompletedTask
            )
        );

        Assert.Equal(PersistedCheck.TimeoutMs / PersistedCheck.PollMs + 1, reads);
    }

    [Theory]
    [InlineData("TE CLI failed (1): cannot connect to localhost")]
    [InlineData("Expected exactly one scalar query result")]
    public async Task ACliFailureOrAResultOfTheWrongShapeFailsAtOnce(string message)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Passes(() =>
            {
                reads++;
                throw new InvalidOperationException(message);
            })
        );

        Assert.Equal(1, reads);
        Assert.Equal(TimeSpan.Zero, clock.Now);
    }

    [Fact]
    public void OnlyTheMetadataScriptsOwnFailureCountsAsNotYetPersisted()
    {
        Assert.True(
            RecipeContext.IsMetadataMismatch(
                new InvalidOperationException("TE CLI failed (1): Metadata assertion failed")
            )
        );
        Assert.False(
            RecipeContext.IsMetadataMismatch(new InvalidOperationException("TE CLI failed (1): cannot connect"))
        );
        Assert.False(RecipeContext.IsMetadataMismatch(new TimeoutException("Metadata assertion failed")));
    }

    [Fact]
    public void AScalarWithAnotherValueIsTypedApartFromAResultOfAnotherShape()
    {
        Assert.Throws<QueryValueMismatchException>(() =>
            QueryAssertions.VerifyScalar("""{"truncated":false,"rows":[{"v":60}]}""", 61)
        );
        var shape = Assert.Throws<InvalidOperationException>(() =>
            QueryAssertions.VerifyScalar("""{"truncated":false,"rows":[]}""", 61)
        );
        Assert.IsNotType<QueryValueMismatchException>(shape);
    }

    private Func<Task<string>> Read(params string[] values) =>
        () => Task.FromResult(values[Math.Min(reads++, values.Length - 1)]);

    [Fact]
    public async Task AChangeAlreadyOnDiskIsReportedFromOneRead()
    {
        var after = await PersistedCheck.Confirmed("before", Read("edited"), clock.Delay);

        Assert.Equal("edited", after);
        Assert.Equal(1, reads);
        Assert.Equal(TimeSpan.Zero, clock.Now);
    }

    [Fact]
    public async Task UnchangedIsConfirmedByOneMoreReadAndALateChangeIsSeen()
    {
        var late = await PersistedCheck.Confirmed("before", Read("before", "edited"), clock.Delay);

        Assert.Equal("edited", late);
        Assert.Equal(2, reads);
        Assert.Equal(PersistedCheck.ConfirmMs, clock.Now.TotalMilliseconds);

        reads = 0;
        Assert.Equal("before", await PersistedCheck.Confirmed("before", Read("before"), clock.Delay));
        Assert.Equal(2, reads);
    }
}
