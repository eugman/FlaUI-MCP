using Xunit;

/// <summary>
/// te3_navigate object over the filtered tree, placed by row depth (TomRows); no desktop access. The live tree is
/// SpaceParts searched for "Brand" on TE3 3.27.1 (2026-10-21): the search also matches role table permissions, which
/// sit under Roles before Tables.
/// </summary>
public sealed class FolderWalkTests
{
    private static RowObservation At(int depth, string value, string? type) => new(value, type, depth);

    // The order TE3 showed: Roles first (with each role's Brands table permission at depth 3, like a folder), then
    // Tables > Brands > 1. Brand Hierarchy > Brand (Node50, depth 4), then another table's Brand column.
    private static FakeTree LiveBrandSearch() =>
        new(
            At(0, "SpaceParts", "Model"),
            At(1, "Roles", null),
            At(2, "Brand VPs", "Role"),
            At(3, "Brands", "Table Permission"),
            At(2, "Business Line Leaders", "Role"),
            At(3, "Brands", "Table Permission"),
            At(1, "Tables", null),
            At(2, "Brands", "Table"),
            At(3, "1. Brand Hierarchy", "Folder"),
            At(4, "Brand", "Column"),
            At(3, "Brand Hierarchy", "Hierarchy"),
            At(4, "Brand", "Level"),
            At(2, "Products", "Table"),
            At(3, "Brand", "Column")
        );

    [Fact]
    public async Task TheLiveWalkWithTheFolderSkipsRolesAndFindsTheColumnInTheFolder()
    {
        // Live, the walk spent its 18 s inside Roles and stopped on Table Permission "Brands".
        var tree = LiveBrandSearch();

        var walk = await PathWalker.Find(tree, "Brands", "Brand", "Column", folder: "1. Brand Hierarchy");

        Assert.True(walk.Found, walk.Reason);
        Assert.Equal("Brands", walk.Table);
        Assert.Equal(["Roles"], tree.Collapsed);
        Assert.Equal(5, tree.Downs);
    }

    [Fact]
    public async Task TheLiveWalkWithoutTheFolderFindsTheSameColumn()
    {
        // Live, the walk reached Node50 but reported "unknown type / table none".
        var walk = await PathWalker.Find(LiveBrandSearch(), "Brands", "Brand", "Column");

        Assert.True(walk.Found, walk.Reason);
        Assert.Equal("Brands", walk.Table);
    }

    [Fact]
    public async Task ATablePermissionIsNeverTakenForTheTable()
    {
        var tree = new FakeTree(At(1, "Roles", null), At(2, "Brand VPs", "Role"), At(3, "Brands", "Table"));

        var walk = await PathWalker.Find(tree, "Brands", "Brands", "Table");

        Assert.False(walk.Found);
    }

    [Fact]
    public async Task OnlyGroupsOtherThanTablesAreCollapsed()
    {
        var tree = LiveBrandSearch();

        await PathWalker.Find(tree, "Products", "Brand", "Column");

        Assert.Equal(["Roles"], tree.Collapsed);
    }

    [Fact]
    public async Task AnObjectInASiblingFolderIsNotTakenAsInsideTheRequestedOne()
    {
        var tree = new FakeTree(
            At(1, "Tables", null),
            At(2, "Brands", "Table"),
            At(3, "1. Brand Hierarchy", "Folder"),
            At(4, "Sub Brand", "Column"),
            At(3, "2. Other", "Folder"),
            At(4, "Brand", "Column")
        );

        var walk = await PathWalker.Find(tree, "Brands", "Brand", "Column", folder: "1. Brand Hierarchy");

        Assert.False(walk.Found);
        Assert.Contains("not inside display folder '1. Brand Hierarchy'", walk.Reason);
    }

    [Fact]
    public async Task AnObjectBackUnderTheTableAfterTheFolderIsNotInIt()
    {
        var tree = new FakeTree(
            At(2, "Brands", "Table"),
            At(3, "1. Brand Hierarchy", "Folder"),
            At(4, "Sub Brand", "Column"),
            At(3, "Brand", "Column")
        );

        var walk = await PathWalker.Find(tree, "Brands", "Brand", "Column", folder: "1. Brand Hierarchy");

        Assert.False(walk.Found);
    }

    [Fact]
    public async Task ANestedFolderIsMatchedOneLevelAtATime()
    {
        var tree = new FakeTree(
            At(2, "Sales", "Table"),
            At(3, "Automation", "Folder"),
            At(4, "Other", "Folder"),
            At(5, "Gross", "Measure"),
            At(4, "Verified", "Folder"),
            At(5, "Net", "Measure")
        );

        var walk = await PathWalker.Find(tree, "Sales", "Net", "Measure", folder: @"Automation\Verified");

        Assert.True(walk.Found, walk.Reason);
    }

    [Fact]
    public async Task ASubfolderOfAnotherFolderWithTheRightNameIsNotThePath()
    {
        // Automation\Other\Verified is not Automation\Verified.
        var tree = new FakeTree(
            At(2, "Sales", "Table"),
            At(3, "Automation", "Folder"),
            At(4, "Other", "Folder"),
            At(5, "Verified", "Folder"),
            At(6, "Net", "Measure")
        );

        var walk = await PathWalker.Find(tree, "Sales", "Net", "Measure", folder: @"Automation\Verified");

        Assert.False(walk.Found);
    }

    [Fact]
    public async Task AWalkThatLeavesTheTableWithoutTheFolderStopsThereAndSaysSo()
    {
        var tree = LiveBrandSearch();

        var walk = await PathWalker.Find(tree, "Brands", "Brand Key", "Column", folder: "2. Other");

        Assert.False(walk.Found);
        Assert.Contains("no display folder '2. Other'", walk.Reason);
    }

    [Fact]
    public async Task TheObjectFoundInAnotherFolderEndsTheWalk()
    {
        var walk = await PathWalker.Find(LiveBrandSearch(), "Brands", "Brand", "Column", folder: "2. Other");

        Assert.False(walk.Found);
        Assert.Contains("not inside display folder '2. Other'", walk.Reason);
    }

    [Fact]
    public async Task AFolderRowNamedLikeAHierarchyIsNotTakenForTheFolder()
    {
        var walk = await PathWalker.Find(LiveBrandSearch(), "Brands", "Brand", "Column", folder: "Brand Hierarchy");

        Assert.False(walk.Found);
    }

    [Fact]
    public async Task ARowWhoseDepthCouldNotBeReadIsNotPlacedInTheFolder()
    {
        var tree = new FakeTree(
            At(2, "Brands", "Table"),
            At(3, "1. Brand Hierarchy", "Folder"),
            new("Brand", "Column")
        );

        var walk = await PathWalker.Find(tree, "Brands", "Brand", "Column", folder: "1. Brand Hierarchy");

        Assert.False(walk.Found);
        Assert.Contains("depth", walk.Reason);
    }

    [Fact]
    public async Task ARowWithNoTypeIsNeverTakenForTheFolder()
    {
        var tree = new FakeTree(At(2, "Brands", "Table"), At(3, "1. Brand Hierarchy", null), At(4, "Brand", "Column"));

        var walk = await PathWalker.Find(tree, "Brands", "Brand", "Column", folder: "1. Brand Hierarchy");

        Assert.False(walk.Found);
    }

    [Fact]
    public async Task AFolderForATableIsRefused() =>
        await Assert.ThrowsAsync<ArgumentException>(() =>
            PathWalker.Find(LiveBrandSearch(), "Brands", "Brands", "Table", folder: "1. Brand Hierarchy")
        );

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

    [Fact]
    public async Task AGroupThatWasAlreadyCollapsedIsNotPressedAgainAndAgain()
    {
        // Left on a collapsed group moves the selection to the model root; the next Down reaches the group again, and
        // the walk pressed Left on it until the clock ran out.
        var tree = new FakeTree(
            At(0, "SpaceParts", "Model"),
            At(1, "Roles", null),
            At(1, "Tables", null),
            At(2, "Brands", "Table"),
            At(3, "Brand", "Column")
        );

        var walk = await PathWalker.Find(tree, "Brands", "Brand", "Column");

        Assert.True(walk.Found, walk.Reason);
        Assert.Equal(["Roles"], tree.Collapsed);
    }

    [Fact]
    public async Task NoGroupIsCollapsedOnceTheClockHasRunOut()
    {
        var tree = LiveBrandSearch();
        var checks = 0;

        await PathWalker.Find(tree, "Brands", "Brand", "Column", outOfTime: () => ++checks > 1);

        Assert.Empty(tree.Collapsed);
    }

    [Fact]
    public async Task AnUntypedRowBackAtTheTablesDepthLeavesTheTable()
    {
        // The walk now reads Properties only for rows that could matter (the object, its table, its folders), so a
        // later table can arrive untyped: its depth alone must end the requested table.
        var tree = new FakeTree(
            At(1, "Tables", null),
            At(2, "Brands", "Table"),
            At(3, "Sub Brand", "Column"),
            At(2, "Products", null),
            At(3, "Brand", "Column")
        );

        var walk = await PathWalker.Find(tree, "Brands", "Brand", "Column");

        Assert.False(walk.Found);
    }

    [Fact]
    public async Task AFolderNestedInAnUntypedOuterFolderIsNotTheRequestedTopLevelFolder()
    {
        // The walk reads Properties only for rows it is looking for, so outer folder X arrives untyped; folder A
        // inside it then matched "A" as if it sat directly under the table, and Brand in X\A was found for "A".
        var tree = new FakeTree(
            At(1, "Tables", null),
            At(2, "Brands", "Table"),
            At(3, "X", "Folder"),
            At(4, "A", "Folder"),
            At(5, "Brand", "Column")
        )
        {
            TypedOnly = ["Brands", "A", "Brand"],
        };

        var walk = await PathWalker.Find(tree, "Brands", "Brand", "Column", folder: "A");

        Assert.False(walk.Found);
    }

    [Fact]
    public async Task ATopLevelFolderStillMatchesWhenOffPathRowsAreUntyped()
    {
        var tree = new FakeTree(
            At(1, "Tables", null),
            At(2, "Brands", "Table"),
            At(3, "X", "Folder"),
            At(4, "Other", "Column"),
            At(3, "A", "Folder"),
            At(4, "Brand", "Column")
        )
        {
            TypedOnly = ["Brands", "A", "Brand"],
        };

        var walk = await PathWalker.Find(tree, "Brands", "Brand", "Column", folder: "A");

        Assert.True(walk.Found, walk.Reason);
    }

    [Fact]
    public async Task AMissAtTheEndOfTheTreeNamesTheRowItEndedOn()
    {
        // Live, a folder miss said "Reached the end of the filtered tree after 3 rows" while it had ended on
        // Products[Sub Brand Name], which read as if it had searched Brands.
        var tree = new FakeTree(
            At(1, "Tables", null),
            At(2, "Products", "Table"),
            At(3, "5. Keys", "Folder"),
            At(4, "Sub Brand Name", "Column")
        );

        var walk = await PathWalker.Find(tree, "Brands", "Brand", "Column", folder: "2. Something");

        Assert.Contains("on 'Sub Brand Name' (Column) under table 'Products'", walk.Reason);
    }
}
