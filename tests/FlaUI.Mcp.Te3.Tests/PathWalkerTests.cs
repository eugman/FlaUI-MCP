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

    [Fact]
    public async Task AChildWalkStopsAtTheRowAfterTheExpandedParent()
    {
        // Sales is expanded and has no Date child; the next table is Date. Pressing Down until any
        // "Date" row is selected used to select that table, outside the parent.
        var tree = new FakeTree(
            new("Sales", "Table"),
            new("Amount", "Data Column"),
            new("Total", "Measure"),
            new("Date", "Calculated Table"),
            new("Date", "Calculated Table Column")
        );
        var walk = await PathWalker.FindRow(tree, "Date", boundary: new("Date", "Calculated Table"), skipCurrent: true);
        Assert.False(walk.Found);
        Assert.Contains("'Date'", walk.Reason);
        Assert.Equal(3, tree.Downs);
    }

    [Fact]
    public async Task AChildNamedLikeTheNextTableIsStillFound()
    {
        // The boundary is the row itself (text and status-bar type), not just its text.
        var walk = await PathWalker.FindRow(
            new FakeTree(new("Sales", "Table"), new("Date", "Data Column"), new("Date", "Calculated Table")),
            "Date",
            boundary: new("Date", "Calculated Table"),
            skipCurrent: true
        );
        Assert.True(walk.Found);
        Assert.Equal(1, walk.RowsVisited);
    }

    [Fact]
    public async Task AFilteredRowIsTheFirstMatchInTreeOrder()
    {
        // A search can show a table and its partition with the same text; the table comes first.
        var tree = new FakeTree(new("Invoices", "Table"), new("Invoices", "Partition"));
        var walk = await PathWalker.FindRow(tree, "Invoices");
        Assert.True(walk.Found);
        Assert.Equal(0, tree.Downs);
    }

    [Fact]
    public async Task AWalkPastTheLastRowSaysSo()
    {
        var walk = await PathWalker.FindRow(new FakeTree(new RowObservation("Tables", "Tables")), "Roles");
        Assert.False(walk.Found);
        Assert.Contains("end of the tree", walk.Reason);
    }

    [Fact]
    public async Task AChildWalkStopsAtTheRowLimitWithoutPressingPastIt()
    {
        var tree = new FakeTree(new("Sales", "Table"), new("Amount", "Data Column"), new("Total", "Measure"));

        var walk = await PathWalker.FindRow(tree, "Date", maxRows: 2);

        Assert.False(walk.Found);
        Assert.Equal(2, walk.RowsVisited);
        Assert.Contains("Stopped after 2 rows", walk.Reason);
        Assert.Equal(1, tree.Downs);
    }
}
