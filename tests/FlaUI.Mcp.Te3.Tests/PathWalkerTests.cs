using Xunit;

/// <summary>The TOM Explorer walk over a scripted tree; no desktop access.</summary>
public sealed class PathWalkerTests
{
    /// <summary>A filtered tree as the keyboard sees it: one selected row at a time.</summary>
    private sealed class FakeTree(params RowObservation[] rows) : ITreeCursor
    {
        private int at;
        public int Downs { get; private set; }

        public Task<RowObservation> Current() => Task.FromResult(rows[at]);

        public Task<RowObservation?> Down()
        {
            Downs++;
            if (at + 1 >= rows.Length)
            {
                return Task.FromResult<RowObservation?>(null);
            }

            at++;
            return Task.FromResult<RowObservation?>(rows[at]);
        }
    }

    // The SpaceParts tree filtered by "Date": relationships, a table group, the Date table and its same-named children,
    // then another table with its own Date column.
    private static FakeTree SpaceParts() =>
        new(
            new("SpaceParts", "Model"),
            new("Relationships", "Relationships"),
            new("'Budget'[Month] ∞←1 'Date'[Date]", "Relationship"),
            new("Tables", "Tables"),
            new("Dimension Tables", "Table Group"),
            new("Date", "Calculated Table"),
            new("RefDate", "Measure"),
            new("Date", "Calculated Table Column"),
            new("Date Hierarchy", "Hierarchy"),
            new("Date", "Level"),
            new("Exchange Rate", "Table"),
            new("Date", "Data Column")
        );

    [Fact]
    public async Task FindsTheTableNotItsSameNamedColumn()
    {
        var tree = SpaceParts();
        var walk = await PathWalker.Find(tree, "Date", "Date", "Table");
        Assert.True(walk.Found);
        Assert.Equal("Date", walk.Table);
        Assert.Equal(6, walk.RowsVisited);
    }

    [Fact]
    public async Task FindsTheColumnNotTheTableOrTheLevel()
    {
        var walk = await PathWalker.Find(SpaceParts(), "Date", "Date", "Column");
        Assert.True(walk.Found);
        Assert.Equal(8, walk.RowsVisited);
    }

    [Fact]
    public async Task ADateColumnUnderAnotherTableIsThatTablesColumn()
    {
        var walk = await PathWalker.Find(SpaceParts(), "Exchange Rate", "Date", "Column");
        Assert.True(walk.Found);
        Assert.Equal("Exchange Rate", walk.Table);
        Assert.Equal(12, walk.RowsVisited);
    }

    [Fact]
    public async Task AMissingObjectReachesTheEndAndSaysSo()
    {
        var walk = await PathWalker.Find(SpaceParts(), "Date", "Date", "Measure");
        Assert.False(walk.Found);
        Assert.Contains("end of the filtered tree", walk.Reason);
    }

    [Fact]
    public async Task TheWalkStopsAtTheRowLimit()
    {
        var tree = SpaceParts();
        var walk = await PathWalker.Find(tree, "Exchange Rate", "Date", "Column", maxRows: 3);
        Assert.False(walk.Found);
        Assert.Equal(2, tree.Downs);
    }
}
