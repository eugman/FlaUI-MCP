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
            return Task.CompletedTask;
        }

        public Task<(IReadOnlyList<TreeRow>? Rows, int? Selected)> ReadSelected() =>
            Task.FromResult<(IReadOnlyList<TreeRow>?, int?)>((All, selected?.Index));

        public Task<(string? Name, string? Type)> Selected() =>
            Task.FromResult<(string?, string?)>(
                selected == null ? (null, null) : (selected.Value, rows[selected.Index].Type)
            );
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
}
