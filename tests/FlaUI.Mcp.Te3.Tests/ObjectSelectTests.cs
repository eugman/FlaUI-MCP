using Xunit;

/// <summary>
/// te3_navigate object's row strategy over scripted rows: names and depth only, one select per candidate, Properties
/// decides. The rows are SpaceParts searched for "Brand" on 3.27.1 (vte3b/vte3d dumps); no desktop access.
/// </summary>
public sealed class ObjectSelectTests
{
    /// <summary>The filtered tree as one read gives it, with the object types Properties shows once a row is selected.</summary>
    private sealed class FakeRows(params (string? Value, int? Depth, string Type)[] rows) : IRowTree
    {
        private TreeRow? selected;

        /// <summary>The row already selected before the walk (an earlier walk left it there).</summary>
        public int? InitiallySelected
        {
            init => selected = value is { } index ? All[index] : null;
        }

        /// <summary>When true, Properties' Name also lags behind the select for the stale reads.</summary>
        public bool PropertiesLate { get; init; }

        public bool Complete { get; init; } = true;

        public bool Measured { get; init; } = true;

        public bool Truncated { get; init; }

        /// <summary>The provider accepts Select and the selection stays where it was.</summary>
        public bool IgnoresSelect { get; init; }

        public List<int> Selects { get; } = [];

        private IReadOnlyList<TreeRow> All => [.. rows.Select((r, i) => new TreeRow(i, r.Value, r.Depth))];

        public Task<RowRead> Read() =>
            Task.FromResult(new RowRead(Measured ? All : null, Complete, Measured ? 24 : null, Truncated));

        public Task Select(TreeRow row)
        {
            Selects.Add(row.Index);
            selected = IgnoresSelect ? selected : row;
            sinceSelect = 0;
            return Task.CompletedTask;
        }

        /// <summary>Rows a read realizes around the selection; null realizes them all.</summary>
        public int? Viewport { get; init; }

        // As TE3 does: only the rows on screen around the selection, not the whole tree.
        public Task<(IReadOnlyList<TreeRow>? Rows, int? Selected)> ReadSelected() =>
            Task.FromResult<(IReadOnlyList<TreeRow>?, int?)>(
                (
                    Viewport is { } size && selected != null
                        ? [.. All.Where(r => r.Index > selected.Index - size && r.Index <= selected.Index)]
                        : All,
                    selected?.Index
                )
            );

        /// <summary>Reads after a select during which the status bar still names the previous selection.</summary>
        public int StaleBarReads { get; init; }

        /// <summary>When true, Properties shows no ObjectType (as for a column on 3.27.1).</summary>
        public bool PropertiesWithoutType { get; init; }

        private string? bar;
        private int sinceSelect;

        public Task<SelectionView> View()
        {
            if (selected != null && sinceSelect++ >= StaleBarReads)
            {
                bar = $"{rows[selected.Index].Type} \"{selected.Value}\"";
            }

            var late = PropertiesLate && sinceSelect <= StaleBarReads;
            var type = selected == null || PropertiesWithoutType || late ? null : rows[selected.Index].Type;
            return Task.FromResult(new SelectionView(bar, late ? null : selected?.Value, type, null));
        }

        public Task Wait(int ms) => Task.CompletedTask;
    }

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
        Assert.Empty(tree.Selects);
    }

    [Fact]
    public async Task WithoutAnIndentNoRowIsPickedAndTheWalkFallsBack()
    {
        var tree = new FakeRows(("Brand", 4, "Column")) { Measured = false };

        var result = await ObjectSelect.Run(tree, NoFolder, () => false);

        Assert.True(result.FallBack);
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
}
