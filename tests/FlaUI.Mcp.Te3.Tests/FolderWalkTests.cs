using Xunit;

/// <summary>te3_navigate object with a display folder: the walk over a scripted tree; no desktop access.</summary>
public sealed class FolderWalkTests
{
    // SpaceParts filtered by "Brand": Brands[Brand] sits in display folder "1. Brand Hierarchy" (UI-MAP), and another
    // table has a same-named column outside any folder.
    private static FakeTree BrandFolders() =>
        new(
            new("Tables", "Tables"),
            new("Brands", "Table"),
            new("Brand Hierarchy", "Hierarchy"),
            new("Brand", "Level"),
            new("1. Brand Hierarchy", "Display Folder"),
            new("Brand", "Data Column"),
            new("Sub Brand", "Data Column"),
            new("Products", "Table"),
            new("Brand", "Data Column")
        );

    [Fact]
    public async Task WithAFolderTheWalkPassesTheTableThenTheFolderThenFindsTheObjectInIt()
    {
        var walk = await PathWalker.Find(BrandFolders(), "Brands", "Brand", "Column", folder: "1. Brand Hierarchy");

        Assert.True(walk.Found);
        Assert.Equal("Brands", walk.Table);
        Assert.Equal(6, walk.RowsVisited);
    }

    [Fact]
    public async Task AnObjectOutsideTheRequestedFolderIsNotTakenForIt()
    {
        var tree = new FakeTree(
            new("Brands", "Table"),
            new("Brand", "Data Column"),
            new("1. Brand Hierarchy", "Display Folder"),
            new("Sub Brand", "Data Column")
        );

        var walk = await PathWalker.Find(tree, "Brands", "Brand", "Column", folder: "1. Brand Hierarchy");

        Assert.False(walk.Found);
        Assert.Contains("not inside display folder '1. Brand Hierarchy'", walk.Reason);
        Assert.Equal(1, tree.Downs);
    }

    [Fact]
    public async Task AWalkThatLeavesTheTableWithoutTheFolderStopsThereAndSaysSo()
    {
        // Without a folder the walk would go on through Products to the end of the tree or the clock.
        var tree = BrandFolders();

        var walk = await PathWalker.Find(tree, "Brands", "Brand Key", "Column", folder: "2. Other");

        Assert.False(walk.Found);
        Assert.Contains("no display folder '2. Other'", walk.Reason);
        Assert.Equal(8, walk.RowsVisited);
        Assert.Equal(7, tree.Downs);
    }

    [Fact]
    public async Task TheObjectFoundInAnotherFolderEndsTheWalk()
    {
        // Object names are unique in a table, and a row's folders come before it in tree order.
        var tree = BrandFolders();

        var walk = await PathWalker.Find(tree, "Brands", "Brand", "Column", folder: "2. Other");

        Assert.False(walk.Found);
        Assert.Contains("not inside display folder '2. Other'", walk.Reason);
        Assert.Equal(5, tree.Downs);
    }

    [Fact]
    public async Task AFolderRowNamedLikeAHierarchyIsNotTakenForTheFolder()
    {
        var tree = new FakeTree(
            new("Brands", "Table"),
            new("Brand Hierarchy", "Hierarchy"),
            new("Brand", "Level"),
            new("Brand", "Data Column")
        );

        var walk = await PathWalker.Find(tree, "Brands", "Brand", "Column", folder: "Brand Hierarchy");

        Assert.False(walk.Found);
    }

    [Fact]
    public async Task AFolderForATableIsRefused() =>
        await Assert.ThrowsAsync<ArgumentException>(() =>
            PathWalker.Find(BrandFolders(), "Brands", "Brands", "Table", folder: "1. Brand Hierarchy")
        );

    [Fact]
    public async Task ANestedFolderIsMatchedOneLevelAtATime()
    {
        var tree = new FakeTree(
            new("Sales", "Table"),
            new("Totals", "Display Folder"),
            new("Gross", "Measure"),
            new("Automation", "Display Folder"),
            new("Verified", "Display Folder"),
            new("Net", "Measure")
        );

        var walk = await PathWalker.Find(tree, "Sales", "Net", "Measure", folder: @"Automation\Verified");

        Assert.True(walk.Found);
        Assert.Equal(6, walk.RowsVisited);
    }

    [Fact]
    public async Task AFolderRowIsMatchedOnlyUnderTheRequestedTable()
    {
        var tree = new FakeTree(
            new("Products", "Table"),
            new("1. Brand Hierarchy", "Display Folder"),
            new("Brands", "Table"),
            new("Brand", "Data Column")
        );

        var walk = await PathWalker.Find(tree, "Brands", "Brand", "Column", folder: "1. Brand Hierarchy");

        Assert.False(walk.Found);
        Assert.Contains("not inside display folder", walk.Reason);
    }

    [Fact]
    public async Task TheRowAfterAParentIsReadOrTheSlowMoveIsSaid()
    {
        // SelectChild reads the row after the parent as the boundary of its subtree. A slow Down read as "the parent
        // is the last row" walked with no boundary and could select a same-named row outside the parent.
        var slow = new FakeTree(new("Sales", "Table"), new("Date", "Calculated Table")) { SlowAt = 0 };
        var last = new FakeTree(new RowObservation("Sales", "Table"));
        var next = new FakeTree(new("Sales", "Table"), new("Date", "Calculated Table"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PathWalker.RowAfter(slow, new("Sales", "Table"))
        );

        Assert.Contains("did not move", error.Message);
        Assert.Null(await PathWalker.RowAfter(last, new("Sales", "Table")));
        Assert.Equal(
            new RowObservation("Date", "Calculated Table"),
            await PathWalker.RowAfter(next, new("Sales", "Table"))
        );
    }

    // Rows with their left edge: TOM Explorer indents a child under its parent.
    private static RowObservation At(double left, string value, string type) => new(value, type, left);

    [Fact]
    public async Task AnObjectInASiblingFolderIsNotTakenAsInsideTheRequestedOne()
    {
        // Leaving "1. Brand Hierarchy" for its sibling "2. Other" did not end the folder, so Brand in "2. Other" was
        // reported inside "1. Brand Hierarchy".
        var tree = new FakeTree(
            At(20, "Brands", "Table"),
            At(40, "1. Brand Hierarchy", "Display Folder"),
            At(60, "Sub Brand", "Data Column"),
            At(40, "2. Other", "Display Folder"),
            At(60, "Brand", "Data Column")
        );

        var walk = await PathWalker.Find(tree, "Brands", "Brand", "Column", folder: "1. Brand Hierarchy");

        Assert.False(walk.Found);
        Assert.Contains("not inside display folder '1. Brand Hierarchy'", walk.Reason);
    }

    [Fact]
    public async Task AnObjectBackUnderTheTableAfterTheFolderIsNotInIt()
    {
        var tree = new FakeTree(
            At(20, "Brands", "Table"),
            At(40, "1. Brand Hierarchy", "Display Folder"),
            At(60, "Sub Brand", "Data Column"),
            At(40, "Brand", "Data Column")
        );

        var walk = await PathWalker.Find(tree, "Brands", "Brand", "Column", folder: "1. Brand Hierarchy");

        Assert.False(walk.Found);
    }

    [Fact]
    public async Task WithIndentationAnObjectDeepInTheFolderIsFound()
    {
        var tree = new FakeTree(
            At(20, "Sales", "Table"),
            At(40, "Automation", "Display Folder"),
            At(60, "Other", "Display Folder"),
            At(80, "Gross", "Measure"),
            At(60, "Verified", "Display Folder"),
            At(80, "Net", "Measure")
        );

        var walk = await PathWalker.Find(tree, "Sales", "Net", "Measure", folder: @"Automation\Verified");

        Assert.True(walk.Found);
    }

    [Fact]
    public async Task ASubfolderOfAnotherFolderWithTheRightNameIsNotThePath()
    {
        // Automation\Other\Verified is not Automation\Verified.
        var tree = new FakeTree(
            At(20, "Sales", "Table"),
            At(40, "Automation", "Display Folder"),
            At(60, "Other", "Display Folder"),
            At(80, "Verified", "Display Folder"),
            At(100, "Net", "Measure")
        );

        var walk = await PathWalker.Find(tree, "Sales", "Net", "Measure", folder: @"Automation\Verified");

        Assert.False(walk.Found);
    }

    [Fact]
    public async Task WithoutIndentationPlacementIsCheckedByRowOrderAndTheReasonSaysSo()
    {
        var tree = new FakeTree(
            At(20, "Brands", "Table"),
            At(20, "Brand", "Data Column"),
            At(20, "1. Brand Hierarchy", "Display Folder")
        );

        var walk = await PathWalker.Find(tree, "Brands", "Brand", "Column", folder: "1. Brand Hierarchy");

        Assert.False(walk.Found);
        Assert.Contains("row order only", walk.Reason);
    }

    [Fact]
    public async Task ARowWithNoTypeIsNeverTakenForTheFolder()
    {
        var tree = new FakeTree(new("Brands", "Table"), new("1. Brand Hierarchy", null), new("Brand", "Data Column"));

        var walk = await PathWalker.Find(tree, "Brands", "Brand", "Column", folder: "1. Brand Hierarchy");

        Assert.False(walk.Found);
    }
}
