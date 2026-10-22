using Xunit;

/// <summary>
/// te3_navigate object's already-selected pick: an object an earlier walk left selected is taken from the first read,
/// before any key, and confirmed by what TE3 names, never selected again. Scripted rows; no desktop access.
/// </summary>
public sealed class AlreadySelectedTests
{
    private static readonly WalkRequest WithFolder = new("Brands", "Brand", "Column", "1. Brand Hierarchy");
    private static readonly WalkRequest NoFolder = new("Brands", "Brand", "Column");

    [Fact]
    public async Task ARowAlreadySelectedIsConfirmedWithoutSelectingItAgain()
    {
        // Live, the column left selected by an earlier walk was turned down ("no new selection") and the level taken.
        var tree = new FakeRows(
            ("Tables", 1, "Group"),
            ("Brands", 2, "Table"),
            ("Brand", 3, "Column"),
            ("Brand Hierarchy", 3, "Hierarchy"),
            ("Brand", 4, "Level")
        )
        {
            InitiallySelected = 2,
        };

        var result = await ObjectSelect.Run(tree, new WalkRequest("Brands", "Brand", ""), () => false);

        Assert.True(result.Found, string.Join("; ", result.Rejected.Select(r => r.Reason)));
        Assert.Equal("Column", result.ConfirmedType);
        Assert.Empty(tree.Selects);
    }

    [Fact]
    public async Task AnObjectAlreadySelectedIsTakenFromTheFirstReadWithNoKeysAndNoSelect()
    {
        // Live (vte3g walk 2): Brands[Brand] left selected by walk 1 still cost a select, because the row read starts
        // with Home, which moves the selection. The first read, before any key, already shows it selected and the
        // status bar names it with its type.
        var tree = new FakeRows(
            ("Tables", 1, "Group"),
            ("Brands", 2, "Table"),
            ("1. Brand Hierarchy", 3, "Folder"),
            ("Brand", 4, "Column"),
            ("Brand Hierarchy", 3, "Hierarchy"),
            ("Brand", 4, "Level")
        )
        {
            InitiallySelected = 3,
            HomeMovesSelection = true,
        };

        var result = await ObjectSelect.Run(tree, WithFolder, () => false);

        Assert.True(result.Found, result.Reason);
        Assert.Equal("Column", result.ConfirmedType);
        Assert.Equal(0, result.Tried);
        Assert.Equal(0, tree.Reads);
        Assert.Empty(tree.Selects);
    }

    [Fact]
    public async Task AFirstReadThatCannotPlaceTheSelectionFallsBackToTheRowRead()
    {
        // The level, not the column, is selected; or the indent cannot be measured in place: read and select as before.
        var level = new FakeRows(
            ("Tables", 1, "Group"),
            ("Brands", 2, "Table"),
            ("1. Brand Hierarchy", 3, "Folder"),
            ("Brand", 4, "Column"),
            ("Brand Hierarchy", 3, "Hierarchy"),
            ("Brand", 4, "Level")
        )
        {
            InitiallySelected = 5,
        };
        var unmeasured = new FakeRows(("Tables", 1, "Group"), ("Brands", 2, "Table"), ("Brand", 3, "Column"))
        {
            InitiallySelected = 2,
            InPlaceMeasured = false,
        };

        Assert.True((await ObjectSelect.Run(level, WithFolder, () => false)).Found);
        Assert.Equal(1, level.Reads);
        Assert.Equal([3], level.Selects);
        Assert.True((await ObjectSelect.Run(unmeasured, NoFolder, () => false)).Found);
        Assert.Equal(1, unmeasured.Reads);
    }

    [Fact]
    public async Task TheObjectTheLastWalkReachedIsTakenFromTheStatusBarWithNoSelectWhenTheRowsCannotPlaceIt()
    {
        // Live (te3-eb2ed6a walk c): Brands[Brand], left selected by walk b, cost a select again because the in-place
        // read could not place it (the root was scrolled out of view, so no depth was known). The host's own last
        // walk confirmed this very request there and TE3 still names the object with its type: no read, no select.
        var tree = new FakeRows(("Tables", 1, "Group"), ("Brands", 2, "Table"), ("Brand", 3, "Column"))
        {
            InitiallySelected = 2,
            InPlaceMeasured = false,
        };

        var result = await ObjectSelect.Run(tree, NoFolder, () => false, lastArrived: NoFolder);

        Assert.True(result.Found, result.Reason);
        Assert.Equal("Column", result.ConfirmedType);
        Assert.Equal(0, result.Tried);
        Assert.Equal(0, tree.Reads);
        Assert.Empty(tree.Selects);
    }

    [Theory]
    [InlineData("Sales", "Brand", "Column", "")] // another table's Brand
    [InlineData("Brands", "Brand", "Measure", "")] // another type: TE3 names a Column
    [InlineData("Brands", "Brand", "Column", "1. Brand Hierarchy")] // a folder the last walk did not confirm
    public async Task TheLastWalksObjectIsNotTakenForAnotherRequest(
        string table,
        string name,
        string type,
        string folder
    )
    {
        var tree = new FakeRows(("Tables", 1, "Group"), ("Brands", 2, "Table"), ("Brand", 3, "Column"))
        {
            InitiallySelected = 2,
            InPlaceMeasured = false,
        };

        var result = await ObjectSelect.Run(tree, new(table, name, type, folder), () => false, lastArrived: NoFolder);

        Assert.Equal(1, tree.Reads);
        Assert.False(result.Found && result.Tried == 0);
    }

    [Fact]
    public async Task TheLastWalksObjectIsNotTakenWhenTe3NamesAnotherSelection()
    {
        // The agent moved the selection in between: the status bar names the level, not the column.
        var tree = new FakeRows(
            ("Tables", 1, "Group"),
            ("Brands", 2, "Table"),
            ("Brand", 3, "Column"),
            ("Brand Hierarchy", 3, "Hierarchy"),
            ("Brand", 4, "Level")
        )
        {
            InitiallySelected = 4,
            InPlaceMeasured = false,
        };

        var result = await ObjectSelect.Run(tree, NoFolder, () => false, lastArrived: NoFolder);

        Assert.Equal(1, tree.Reads);
        Assert.Equal([2], tree.Selects);
        Assert.True(result.Found, result.Reason);
    }
}
