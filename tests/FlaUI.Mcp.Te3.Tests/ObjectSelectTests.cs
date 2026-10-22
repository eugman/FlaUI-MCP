using Xunit;

/// <summary>
/// te3_navigate object's row strategy over scripted rows: names and depth only, one select per candidate, Properties
/// decides. The rows are SpaceParts searched for "Brand" on 3.27.1 (vte3b/vte3d dumps); no desktop access.
/// </summary>
public sealed class ObjectSelectTests
{
    private static FakeRows LiveBrandSearch(bool complete = true) =>
        new(
            ("SpaceParts", 0, "Model"),
            ("Roles", 1, "Group"),
            ("Brand VPs", 2, "Role"),
            ("Brands", 3, "Table Permission"),
            ("Tables", 1, "Group"),
            ("Brands", 2, "Table"),
            ("1. Brand Hierarchy", 3, "Folder"),
            ("Brand", 4, "Column"),
            ("Brand Hierarchy", 3, "Hierarchy"),
            ("Brand", 4, "Level"),
            ("3. Managers", 3, "Folder"),
            ("Products", 2, "Table"),
            ("5. Keys", 3, "Folder"),
            ("Sub Brand Name", 4, "Column")
        )
        {
            Complete = complete,
        };

    private static readonly WalkRequest WithFolder = new("Brands", "Brand", "Column", "1. Brand Hierarchy");
    private static readonly WalkRequest NoFolder = new("Brands", "Brand", "Column");

    [Fact]
    public async Task WithTheFolderTheColumnIsSelectedInOneInput()
    {
        // Live, the keyboard walk spent 20 s and 17 inputs and stopped 2 rows short.
        var tree = LiveBrandSearch();

        var result = await ObjectSelect.Run(tree, WithFolder, () => false);

        Assert.True(result.Found, result.Reason);
        Assert.Equal([7], tree.Selects);
        Assert.Equal(14, result.RowsRead);
    }

    [Fact]
    public async Task WithoutTheFolderTheSameNamedLevelIsTriedOnlyAfterTheColumn()
    {
        var tree = LiveBrandSearch();

        var result = await ObjectSelect.Run(tree, NoFolder, () => false);

        Assert.True(result.Found, result.Reason);
        Assert.Equal([7], tree.Selects);
    }

    [Fact]
    public async Task PropertiesDecideBetweenAColumnAndALevelOfTheSameName()
    {
        // The level comes first here; Properties says Level, so the next candidate is selected.
        var tree = new FakeRows(
            ("Tables", 1, "Group"),
            ("Brands", 2, "Table"),
            ("Brand Hierarchy", 3, "Hierarchy"),
            ("Brand", 4, "Level"),
            ("Brand", 3, "Column")
        );

        var result = await ObjectSelect.Run(tree, NoFolder, () => false);

        Assert.True(result.Found);
        Assert.Equal([3, 4], tree.Selects);
        Assert.Equal(2, result.Tried);
    }

    [Fact]
    public async Task ATablePermissionUnderARoleIsNeverTakenForTheTable()
    {
        var tree = new FakeRows(("Roles", 1, "Group"), ("Brand VPs", 2, "Role"), ("Brands", 3, "Table Permission"));

        var result = await ObjectSelect.Run(tree, new("Brands", "Brands", "Table"), () => false);

        Assert.False(result.Found);
        Assert.Empty(tree.Selects);
    }

    [Fact]
    public async Task AnObjectInAnotherFolderIsNotACandidate()
    {
        var tree = LiveBrandSearch();

        var result = await ObjectSelect.Run(tree, WithFolder with { Folder = "3. Managers" }, () => false);

        Assert.False(result.Found);
        Assert.Empty(tree.Selects);
        Assert.False(result.FallBack);
        Assert.Contains("among the 14 rows", result.Reason);
    }

    [Fact]
    public async Task ANestedSameNamedFolderIsNotTheTopLevelOne()
    {
        var tree = new FakeRows(
            ("Tables", 1, "Group"),
            ("Brands", 2, "Table"),
            ("X", 3, "Folder"),
            ("A", 4, "Folder"),
            ("Brand", 5, "Column")
        );

        var result = await ObjectSelect.Run(tree, NoFolder with { Folder = "A" }, () => false);

        Assert.False(result.Found);
        Assert.Empty(tree.Selects);
    }

    [Fact]
    public async Task ATreeThatMayHoldUnrealizedRowsFallsBackToTheKeyboardWalk()
    {
        // treeList offers no Scroll pattern (vte3b finding 3), so rows past the viewport are reached with keys.
        var tree = new FakeRows(("Tables", 1, "Group"), ("Brands", 2, "Table")) { Complete = false };

        var result = await ObjectSelect.Run(tree, NoFolder, () => false);

        Assert.True(result.FallBack);
        Assert.True(ObjectSelect.NeedsKeys(result));
        Assert.Empty(tree.Selects);
    }

    [Fact]
    public async Task WithoutAnIndentNoRowIsPickedTheTableIsTriedAndTheKeyboardWalkIsNot()
    {
        // Live (te3-eb2ed6a walk f): the name and table searches both placed no row, and the keyboard walk that
        // followed read 0 rows, spent 25 inputs and 23 s and stopped on Group "Tables". A read that placed no row is
        // the second try's reason, never the keyboard walk's: it would start from nothing.
        var tree = new FakeRows(("Brand", 4, "Column")) { Measured = false };

        var result = await ObjectSelect.Run(tree, NoFolder, () => false);

        Assert.True(result.FallBack);
        Assert.False(ObjectSelect.NeedsKeys(result));
        Assert.Contains("The search for 'Brand' placed no row", result.Reason);
        Assert.Contains("no row was placed, so the keyboard walk was not tried", ObjectSelect.EmptyReadMiss(result));
        Assert.Empty(tree.Selects);
    }

    [Fact]
    public async Task AMissNamesTheRowTheSelectionEndedOn()
    {
        var tree = new FakeRows(("Tables", 1, "Group"), ("Brands", 2, "Table"), ("Brand", 3, "Measure"));

        var result = await ObjectSelect.Run(tree, NoFolder, () => false);

        Assert.False(result.Found);
        Assert.Contains("row 2 'Brand'", result.Reason);
    }

    [Fact]
    public async Task NoSelectIsSentOnceTheClockHasRunOut()
    {
        var tree = LiveBrandSearch();

        var result = await ObjectSelect.Run(tree, NoFolder, () => true);

        Assert.False(result.Found);
        Assert.Empty(tree.Selects);
    }

    [Fact]
    public async Task ARowOfUnknownDepthEndsTheTableSoAnotherTablesObjectIsNoCandidate()
    {
        // Products' row gave no depth; without it the scan stayed "inside Brands" and Products' Brand was picked.
        var tree = new FakeRows(
            ("Tables", 1, "Group"),
            ("Brands", 2, "Table"),
            ("Products", null, "Table"),
            ("Brand", 3, "Column")
        );

        var result = await ObjectSelect.Run(tree, NoFolder, () => false);

        Assert.False(result.Found);
        Assert.Empty(tree.Selects);
        Assert.True(result.FallBack);
    }

    [Fact]
    public async Task ASelectThatDidNotMoveTheSelectionIsRejectedBeforePropertiesAreTrusted()
    {
        // Properties still showed the previous object; a fresh read of the rows says where the selection really is.
        var tree = new FakeRows(("Tables", 1, "Group"), ("Brands", 2, "Table"), ("Brand", 3, "Column"))
        {
            IgnoresSelect = true,
        };

        var result = await ObjectSelect.Run(tree, NoFolder, () => false);

        Assert.False(result.Found);
        Assert.Contains("is not row 2", Assert.Single(result.Rejected).Reason);
    }

    [Fact]
    public async Task EachRejectedCandidateIsListedWithWhatPropertiesShowed()
    {
        var tree = new FakeRows(
            ("Tables", 1, "Group"),
            ("Brands", 2, "Table"),
            ("Brand Hierarchy", 3, "Hierarchy"),
            ("Brand", 4, "Level"),
            ("Brand", 3, "Column")
        );

        var result = await ObjectSelect.Run(tree, NoFolder, () => false);

        Assert.True(result.Found);
        var rejected = Assert.Single(result.Rejected);
        Assert.Equal(3, rejected.Index);
        Assert.Contains("Level", rejected.Reason);
    }

    [Fact]
    public async Task ATruncatedReadIsReportedAndFallsBackWhenItHoldsNoCandidate()
    {
        var found = await ObjectSelect.Run(LiveBrandSearchTruncated(), NoFolder, () => false);
        var missing = await ObjectSelect.Run(
            new FakeRows(("Tables", 1, "Group"), ("Brands", 2, "Table")) { Truncated = true },
            NoFolder,
            () => false
        );

        Assert.True(found.Found);
        Assert.True(found.Truncated);
        Assert.True(missing.FallBack);
        Assert.True(missing.Truncated);
    }

    private static FakeRows LiveBrandSearchTruncated() =>
        new(("Tables", 1, "Group"), ("Brands", 2, "Table"), ("1. Brand Hierarchy", 3, "Folder"), ("Brand", 4, "Column"))
        {
            Truncated = true,
        };

    [Fact]
    public async Task WithoutAnObjectTypeTheFirstNamedCandidateTeConfirmsIsTakenAndItsTypeReported()
    {
        // A hierarchy level has no type the agent knows; the status bar names it once selected.
        var tree = new FakeRows(
            ("Tables", 1, "Group"),
            ("Brands", 2, "Table"),
            ("Brand Hierarchy", 3, "Hierarchy"),
            ("Brand", 4, "Level")
        );

        var result = await ObjectSelect.Run(tree, new WalkRequest("Brands", "Brand", ""), () => false);

        Assert.True(result.Found, result.Reason);
        Assert.Equal("Level", result.ConfirmedType);
    }

    [Fact]
    public async Task ARowPickedOnALaterPageIsPlacedOnThePagedRowsNotTheFreshScreenAlone()
    {
        // The fresh read after the select shows only the screen around the row; Tables and the table row are on
        // page 0, so placing the row on that read alone turned the right column down.
        var rows = new List<(string?, int?, string)> { ("Tables", 1, "Group"), ("Brands", 2, "Table") };
        rows.AddRange(Enumerable.Range(0, 30).Select(i => ((string?)$"Other {i}", (int?)3, "Column")));
        rows.Add(("Brand", 3, "Column"));
        var tree = new FakeRows([.. rows]) { Viewport = 5 };

        var result = await ObjectSelect.Run(tree, NoFolder, () => false);

        Assert.True(result.Found, string.Join("; ", result.Rejected.Select(r => r.Reason)));
    }

    private static FakeRows LevelThenColumn(int staleBarReads) =>
        new(
            ("Tables", 1, "Group"),
            ("Brands", 2, "Table"),
            ("Brand Hierarchy", 3, "Hierarchy"),
            ("Brand", 4, "Level"),
            ("Brand", 3, "Column")
        )
        {
            StaleBarReads = staleBarReads,
            PropertiesWithoutType = true,
        };

    [Fact]
    public async Task AStatusBarSlotLeftByTheEarlierSameNamedCandidateIsWaitedOutNotReported()
    {
        // After the Level "Brand" was turned down, the slot still read Level "Brand" when the column was selected.
        var result = await ObjectSelect.Run(LevelThenColumn(staleBarReads: 2), NoFolder, () => false);

        Assert.True(result.Found, string.Join("; ", result.Rejected.Select(r => r.Reason)));
        Assert.Equal("Column", result.ConfirmedType);
    }

    [Fact]
    public async Task ATypeTeNeverShowsIsAMismatchNotAColumn()
    {
        // The slot never updates and Properties has no ObjectType for a column: nothing proves it is a Column.
        var result = await ObjectSelect.Run(LevelThenColumn(staleBarReads: 100), NoFolder, () => false);

        Assert.False(result.Found);
        Assert.All(result.Rejected, r => Assert.Contains("not Column", r.Reason));
    }

    [Theory]
    [InlineData("Column \"Brand\"", null, "Column")] // the slot names the label: its type
    [InlineData("Column \"Type\"", "Brand", null)] // a folder or stale slot: Properties' Name, no ObjectType
    [InlineData("Column \"Type\"", "Type", "none")] // neither names the label
    public void TeNamesTheSelectionBySlotFirstThenPropertiesName(string bar, string? name, string? type)
    {
        var seen = SelectionView.Read(new TreeRow(4, "Brand", 3), new SelectionView(bar, name, null, null));

        if (type == "none")
        {
            Assert.Null(seen);
        }
        else
        {
            Assert.Equal("Brand", seen?.Name);
            Assert.Equal(type, seen?.Type);
        }
    }

    [Fact]
    public async Task ASelectThatTakesAMomentToShowIsWaitedFor()
    {
        // Live, Measure "Order Lines" showed in the status bar only after the first reading.
        var tree = new FakeRows(("Tables", 1, "Group"), ("Orders", 2, "Table"), ("Order Lines", 3, "Measure"))
        {
            StaleBarReads = 3,
            PropertiesLate = true,
        };

        var result = await ObjectSelect.Run(tree, new WalkRequest("Orders", "Order Lines", "Measure"), () => false);

        Assert.True(result.Found, string.Join("; ", result.Rejected.Select(r => r.Reason)));
    }

    [Fact]
    public async Task AnEmptyReadIsAMissNeverAFallBackToKeys()
    {
        // Live, a read before the filter rendered had 0 rows, and the key walk that followed went wrong.
        var tree = new FakeRows { Complete = false };

        var result = await ObjectSelect.Run(tree, NoFolder, () => false);

        Assert.False(result.Found);
        Assert.False(result.FallBack);
        Assert.Contains("no rows", result.Reason);
    }

    [Fact]
    public void AMissSaysWhatWasAskedForAndWhereTheSelectionEnded()
    {
        // Live: "No  'Brand'" (empty type), "nothing was selected" while the selection had moved to Table "Products",
        // and keys misses "under table none" though a table row sat above the selection.
        TreeRow[] rows =
        [
            new(0, "Tables", 1),
            new(1, "Brands", 2),
            new(2, "Brand", 3),
            new(3, "Products", 2),
            new(4, "5. Keys", 3),
            new(5, "Sub Brand Name", 4),
        ];

        var text = WalkMiss.Text(
            new WalkRequest("Brands", "Brand", "", "2. Something"),
            "No row named 'Brand' in place.",
            rows[5],
            SearchPick.TableOf(rows, 5),
            8
        );

        Assert.Contains("No object 'Brand' under table 'Brands' in folder '2. Something'", text);
        Assert.Contains("ended on row 5 'Sub Brand Name' under table 'Products'", text);
        Assert.Equal("Brands", SearchPick.TableOf(rows, 2));
        Assert.Null(SearchPick.TableOf(rows, 0));
    }

    [Fact]
    public void AMissSaysWhereTe3LeftTheSelectionAfterTheSearchCleared()
    {
        // Live (te3-eb2ed6a walk g): the folder miss ended on row 19 'Products' in the filtered tree and, after the
        // clear, the status bar read Table "Products": TE3 moves the selection when the filtered tree is replaced,
        // with no select from the walk. The reply says so, or that it stayed, or that the bar names nothing.
        Assert.Contains(
            "the selection is Table \"Products\": TE3 moved it when the filtered tree was replaced; the walk did not",
            WalkMiss.AfterClear("Table \"Products\"", "Partitions")
        );
        Assert.Contains("stayed on Table \"Products\"", WalkMiss.AfterClear("Table \"Products\"", "Products"));
        Assert.Contains("names no selection", WalkMiss.AfterClear(null, "Products"));
    }
}
