using Xunit;

/// <summary>Waiting for the filtered tree to render after the search is typed; scripted counts, no desktop.</summary>
public sealed class RowSettleTests
{
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
