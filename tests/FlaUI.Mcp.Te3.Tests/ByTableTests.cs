using Xunit;

/// <summary>
/// The row strategy's second try (vte3g walks 4 and 6): when the object's name shows no placed candidate, the tree is
/// filtered by the table's name, the table row is selected and expanded, and the object is looked for under it.
/// Scripted rows; no desktop access.
/// </summary>
public sealed class ByTableTests
{
    /// <summary>A filtered tree whose rows depend on the search text and on which rows were opened (Right).</summary>
    private sealed class SearchedTree(
        Func<string, IReadOnlySet<string>, IReadOnlyList<(string Value, int? Depth, string Type)>> rowsFor,
        string firstSearch
    ) : IRowTree
    {
        private string search = firstSearch;
        private readonly HashSet<string> opened = [];
        private TreeRow? selected;

        public List<string> Searches { get; } = [];
        public List<int> Selects { get; } = [];

        /// <summary>The rows opened with Right, in order (a row with nothing under it shows nothing more).</summary>
        public List<string> Opened { get; } = [];

        private IReadOnlyList<(string Value, int? Depth, string Type)> Now => rowsFor(search, opened);

        private IReadOnlyList<TreeRow> All => [.. Now.Select((r, i) => new TreeRow(i, r.Value, r.Depth))];

        private bool Measured => Now.All(r => r.Depth != null);

        public Task<RowRead> Read() =>
            Task.FromResult(new RowRead(Measured ? All : null, true, Measured ? 24 : null, false));

        public Task Select(TreeRow row)
        {
            Selects.Add(row.Index);
            selected = row;
            return Task.CompletedTask;
        }

        public Task<(IReadOnlyList<TreeRow>? Rows, int? Selected)> ReadSelected() =>
            Task.FromResult<(IReadOnlyList<TreeRow>?, int?)>((Measured ? All : null, selected?.Index));

        public Task<SelectionView> View()
        {
            var type = selected == null ? null : Now[selected.Index].Type;
            return Task.FromResult(
                new SelectionView(selected == null ? null : $"{type} \"{selected.Value}\"", selected?.Value, type, null)
            );
        }

        public Task Wait(int ms) => Task.CompletedTask;

        public Task Search(string text)
        {
            Searches.Add(text);
            (search, selected) = (text, null);
            opened.Clear();
            return Task.CompletedTask;
        }

        public Task Expand()
        {
            Opened.Add(selected?.Value ?? "");
            opened.Add(selected?.Value ?? "");
            return Task.CompletedTask;
        }
    }

    // "Rate" matches Tax Rate, the calculation items and Budget Rate's column; the filtered view it gave live had no
    // measurable indent. Filtering by "Budget Rate" shows the table, and expanding it its columns.
    private static IReadOnlyList<(string, int?, string)> Rows(string search, IReadOnlySet<string> opened)
    {
        if (search != "Budget Rate")
        {
            return
            [
                ("Value (Budget Rate)", null, "Calculation Item"),
                ("Tax Rate", null, "Column"),
                ("Rate", null, "Column"),
            ];
        }

        List<(string, int?, string)> rows =
        [
            ("SpaceParts", 0, "Model"),
            ("Tables", 1, "Group"),
            ("Budget Rate", 2, "Table"),
        ];
        if (opened.Contains("Budget Rate"))
        {
            rows.AddRange([("1. Facts", 3, "Folder"), ("Rate", 4, "Column"), ("Currency", 3, "Column")]);
        }

        return rows;
    }

    // Live (te3-eb2ed6a walk e): filtered by the table's name and opened, the table shows Partitions and its display
    // folders collapsed; Rate sits inside "1. Facts", so the 12 rows read held no row named Rate.
    private static IReadOnlyList<(string, int?, string)> Collapsed(string search, IReadOnlySet<string> opened)
    {
        if (search != "Budget Rate")
        {
            return [("Value (Budget Rate)", null, "Calculation Item"), ("Rate", null, "Column")];
        }

        List<(string, int?, string)> rows =
        [
            ("SpaceParts", 0, "Model"),
            ("Tables", 1, "Group"),
            ("Budget Rate", 2, "Table"),
        ];
        if (!opened.Contains("Budget Rate"))
        {
            return rows;
        }

        rows.Add(("Partitions", 3, "Group"));
        if (opened.Contains("Partitions"))
        {
            rows.Add(("Budget Rate", 4, "Partition"));
        }

        rows.Add(("Currency", 3, "Column"));
        rows.Add(("1. Facts", 3, "Folder"));
        if (opened.Contains("1. Facts"))
        {
            rows.Add(("Rate", 4, "Column"));
        }

        rows.Add(("2. Keys", 3, "Folder"));
        rows.Add(("Customers", 2, "Table"));
        return rows;
    }

    [Fact]
    public async Task AnObjectInsideACollapsedDisplayFolderIsFoundByOpeningTheRowsUnderTheTableInTurn()
    {
        var tree = new SearchedTree(Collapsed, "Rate");

        var result = await ObjectSelect.ByTable(tree, new WalkRequest("Budget Rate", "Rate", ""), () => false);

        // Review: Partitions never holds a column or measure, so it is skipped by name; the column Currency is
        // selected, typed Column by TE3 and left alone (no Right); the folder is opened and shows Rate.
        Assert.True(result.Found, result.Reason);
        Assert.Equal("Column", result.ConfirmedType);
        Assert.Equal(["Budget Rate", "1. Facts"], tree.Opened);
        Assert.Equal([2, 4, 5, 6], tree.Selects);
        Assert.Equal(4, result.Tried);
        Assert.True(result.ByTable);
    }

    [Fact]
    public async Task WithAFolderPathOnlyItsFoldersAreOpened()
    {
        var tree = new SearchedTree(Collapsed, "Rate");

        var result = await ObjectSelect.ByTable(
            tree,
            new WalkRequest("Budget Rate", "Rate", "Column", "1. Facts"),
            () => false
        );

        Assert.True(result.Found, result.Reason);
        Assert.Equal(["Budget Rate", "1. Facts"], tree.Opened);
    }

    // A table with more empty folders than the walk may open.
    private static IReadOnlyList<(string, int?, string)> ManyFolders(string search, IReadOnlySet<string> opened)
    {
        List<(string, int?, string)> rows =
        [
            ("SpaceParts", 0, "Model"),
            ("Tables", 1, "Group"),
            ("Budget Rate", 2, "Table"),
        ];
        if (search == "Budget Rate" && opened.Contains("Budget Rate"))
        {
            rows.AddRange(Enumerable.Range(1, 6).Select(i => ($"Folder {i}", (int?)3, "Folder")));
        }

        return rows;
    }

    [Fact]
    public async Task FolderOpensAreBoundedByTheirCountAndTheClockAndTheMissNamesThem()
    {
        var bounded = new SearchedTree(ManyFolders, "Rate");
        var stopped = new SearchedTree(ManyFolders, "Rate");
        var request = new WalkRequest("Budget Rate", "Rate", "Column");

        var miss = await ObjectSelect.ByTable(bounded, request, () => false);
        var clock = await ObjectSelect.ByTable(stopped, request, () => true);

        Assert.False(miss.Found);
        Assert.Equal(1 + ObjectSelect.MaxFolderOpens, bounded.Opened.Count);
        Assert.Contains($"{ObjectSelect.MaxFolderOpens} folder(s) under it opened (Folder 1, Folder 2", miss.Reason);
        Assert.Contains("4 row(s) were selected", miss.Reason);
        Assert.Equal(["Budget Rate"], stopped.Opened);
        Assert.False(clock.Found);
    }

    [Fact]
    public async Task AnObjectTheNameSearchCannotPlaceIsFoundUnderItsTableFilteredByName()
    {
        var tree = new SearchedTree(Rows, "Rate");
        var request = new WalkRequest("Budget Rate", "Rate", "Column");

        var byName = await ObjectSelect.Run(tree, request, () => false);
        Assert.True(byName.FallBack);

        var result = await ObjectSelect.ByTable(tree, request, () => false);

        Assert.True(result.Found, result.Reason);
        Assert.Equal("Column", result.ConfirmedType);
        Assert.Equal(["Budget Rate"], tree.Searches);
        Assert.Equal(["Budget Rate"], tree.Opened);
        Assert.Equal([2, 4], tree.Selects);
        Assert.True(result.ByTable);
    }

    [Fact]
    public async Task ATableTheSearchDoesNotShowIsAMissThatFallsBackWithNoSelect()
    {
        var tree = new SearchedTree((_, _) => [("Tables", 1, "Group"), ("Products", 2, "Table")], "Rate");

        var result = await ObjectSelect.ByTable(tree, new WalkRequest("Budget Rate", "Rate", "Column"), () => false);

        Assert.False(result.Found);
        Assert.True(result.FallBack);
        Assert.Contains("Budget Rate", result.Reason);
        Assert.Empty(tree.Selects);
        Assert.Empty(tree.Opened);
    }
}
