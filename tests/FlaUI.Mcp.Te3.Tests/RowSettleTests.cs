using Xunit;

/// <summary>Waiting for the filtered tree to render after the search is typed; scripted counts, no desktop.</summary>
public sealed class RowSettleTests
{
    [Fact]
    public async Task SlowReadConsumesTheStableObservationWallClockBudget()
    {
        var reads = 0;
        var result = await RowSettle.WaitStable(
            async () =>
            {
                reads++;
                await Task.Delay(60);
                return "ready";
            },
            _ => Task.CompletedTask,
            budgetMs: 20,
            pollMs: 5
        );

        Assert.Null(result);
        Assert.Equal(1, reads);
    }

    [Fact]
    public async Task NavigationDeadlineStopsStableReadsAfterOneObservation()
    {
        var reads = 0;
        var result = await RowSettle.WaitStable(
            () => Task.FromResult<string?>(++reads == 1 ? "ready" : "late"),
            _ => Task.CompletedTask,
            budgetMs: 900,
            pollMs: 150,
            outOfTime: () => reads > 0
        );

        Assert.Null(result);
        Assert.Equal(1, reads);
    }

    private static Func<Task<int>> Counts(params int[] counts)
    {
        var next = 0;
        return () => Task.FromResult(counts[Math.Min(next++, counts.Length - 1)]);
    }

    [Fact]
    public async Task TheRowsAreReadOnceTwoReadsAgreeOnANonEmptyTree()
    {
        // Live, the first read came before the filter rendered: 0 rows, and the walk fell back to keys.
        var waited = 0;

        var count = await RowSettle.Wait(Counts(0, 0, 14, 22, 22), ms => Task.FromResult(waited += ms), 1500, 250);

        Assert.Equal(22, count);
        Assert.Equal(1000, waited);
    }

    [Fact]
    public async Task AnEmptyTreeIsWaitedOnForTheBudgetOnly()
    {
        var waited = 0;

        var count = await RowSettle.Wait(Counts(0), ms => Task.FromResult(waited += ms), 1500, 250);

        Assert.Equal(0, count);
        Assert.Equal(1500, waited);
    }
}
