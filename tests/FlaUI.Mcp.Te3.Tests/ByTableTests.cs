using Xunit;

/// <summary>
/// The row strategy's second try (vte3g walks 4 and 6; te3b-24/26/28): when the object's name shows no placed
/// candidate, the tree is filtered by the table's name, the table row selected and confirmed, the filter cleared, and
/// the object looked for in the rows below the table with its folders opened in turn. Scripted rows; no desktop
/// access. The rows model TE3's filter as seen live: a filtered row shows only when its name contains the text (its
/// ancestors with it), and the cleared tree keeps the expansions the filter and the walk made.
/// </summary>
public sealed class ByTableTests
{
    /// <summary>A tree whose rows depend on the search text and on which rows are open.</summary>
    private sealed class SearchedTree(
        Func<string, IReadOnlySet<string>, IReadOnlyList<(string Value, int? Depth, string Type)>> rowsFor,
        string firstSearch,
        IEnumerable<string>? initiallyOpen = null
    ) : IRowTree
    {
        private string search = firstSearch;
        private readonly HashSet<string> opened = [.. initiallyOpen ?? []];
        private TreeRow? selected;

        public List<string> Searches { get; } = [];
        public List<int> Selects { get; } = [];

        /// <summary>The rows opened with Right, in order (a row with nothing under it shows nothing more).</summary>
        public List<string> Opened { get; } = [];

        /// <summary>False when the tree scrolls, so rows may be unrealized (the read then falls back).</summary>
        public bool Complete { get; init; } = true;

        private IReadOnlyList<(string Value, int? Depth, string Type)> Now => rowsFor(search, opened);

        private IReadOnlyList<TreeRow> All => [.. Now.Select((r, i) => new TreeRow(i, r.Value, r.Depth))];

        private bool Measured => Now.Count > 0 && Now.All(r => r.Depth != null);

        public Task<RowRead> Read() =>
            Task.FromResult(new RowRead(Measured ? All : null, Complete, Measured ? 24 : null, false));

        public Task Select(TreeRow row)
        {
            Selects.Add(row.Index);
            selected = row;
            return Task.CompletedTask;
        }

        public Task<(IReadOnlyList<TreeRow>? Rows, int? Selected)> ReadSelected() =>
            Task.FromResult<(IReadOnlyList<TreeRow>?, int?)>((Measured ? All : null, selected?.Index));

        public Task<(RowRead Read, int? Selected)> ReadBelow() =>
            Task.FromResult(
                (new RowRead(Measured ? All : null, Complete, Measured ? 24 : null, false), selected?.Index)
            );

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
            return Task.CompletedTask;
        }

        // As TE3: the whole tree shows again, the selection kept (its index changes with the rows).
        public Task ClearSearch()
        {
            var (value, depth) = (selected?.Value, selected?.Depth);
            search = "";
            selected = All.FirstOrDefault(r => r.Value == value && r.Depth == depth);
            return Task.CompletedTask;
        }

        public Task Expand()
        {
            Opened.Add(selected?.Value ?? "");
            opened.Add(selected?.Value ?? "");
            return Task.CompletedTask;
        }
    }

    private static readonly (string, int?, string) SpaceParts = ("SpaceParts", 0, "Model");
    private static readonly (string, int?, string) Tables = ("Tables", 1, "Group");

    // SpaceParts as the live runs showed it. Filtered by "Budget Rate" or "Exchange Rate": the table, its Partitions
    // group with the partition named after the table, and (Exchange Rate) a folder whose column's name contains the
    // text; the folders' other rows never show. Filtered by "Rate": no measurable indent (te3 walk e). Cleared: the
    // whole tree, the tables' folders open only when opened.
    private static IReadOnlyList<(string, int?, string)> Live(string search, IReadOnlySet<string> open)
    {
        switch (search)
        {
            case "Budget Rate":
                return
                [
                    SpaceParts,
                    Tables,
                    ("Budget Rate", 2, "Table"),
                    ("Partitions", 3, "Group"),
                    ("Budget Rate", 4, "Partition"),
                    ("Z01CG2 - Value (Budget Rate)", 2, "Table"),
                    ("Value (Budget Rate)", 3, "Calculation Item"),
                ];
            case "Exchange Rate":
                return
                [
                    SpaceParts,
                    Tables,
                    ("Exchange Rate", 2, "Table"),
                    ("Partitions", 3, "Group"),
                    ("Exchange Rate", 4, "Partition"),
                    ("2. Other Currency Fields", 3, "Folder"),
                    ("Exchange Rate Composite Key", 4, "Column"),
                ];
            case "":
                return Cleared(open);
            default:
                return
                [
                    ("Value (Budget Rate)", null, "Calculation Item"),
                    ("Tax Rate", null, "Column"),
                    ("Rate", null, "Column"),
                ];
        }
    }

    private static IReadOnlyList<(string, int?, string)> Cleared(IReadOnlySet<string> open)
    {
        List<(string, int?, string)> rows = [SpaceParts, Tables, ("Brands", 2, "Table"), ("Budget Rate", 2, "Table")];
        if (open.Contains("Budget Rate"))
        {
            rows.Add(("Partitions", 3, "Group"));
            rows.AddRange(open.Contains("Partitions") ? [("Budget Rate", 4, "Partition")] : []);
            rows.Add(("1. Facts", 3, "Folder"));
            rows.AddRange(open.Contains("1. Facts") ? [("Rate", 4, "Column")] : []);
            rows.Add(("2. Keys", 3, "Folder"));
            rows.AddRange(open.Contains("2. Keys") ? [("Currency Key", 4, "Column")] : []);
            rows.Add(("3. Other Currency Fields", 3, "Folder"));
        }

        rows.Add(("Customers", 2, "Table"));
        rows.Add(("Exchange Rate", 2, "Table"));
        if (open.Contains("Exchange Rate"))
        {
            rows.Add(("Partitions", 3, "Group"));
            rows.Add(("2. Other Currency Fields", 3, "Folder"));
            if (open.Contains("2. Other Currency Fields"))
            {
                rows.AddRange([
                    ("Currency System", 4, "Column"),
                    ("Date", 4, "Column"),
                    ("Exchange Rate Composite Key", 4, "Column"),
                    ("Month", 4, "Column"),
                    ("Rate", 4, "Column"),
                ]);
            }
        }

        rows.Add(("Z01CG2 - Value (Budget Rate)", 2, "Table"));
        return rows;
    }

    [Fact]
    public async Task AnObjectHiddenByTheTableNameFilterIsFoundBelowTheTableOnceTheSearchClears()
    {
        // te3b-24: Budget Rate[Rate] missed under the filter (Rate is in 1. Facts, which the filter hid). The table
        // row is selected and confirmed, the search cleared, Partitions skipped, 1. Facts probed (a folder) and
        // opened, and Rate selected under it.
        var tree = new SearchedTree(Live, "Rate", ["Budget Rate", "Partitions"]);

        var result = await ObjectSelect.ByTable(tree, new WalkRequest("Budget Rate", "Rate", ""), () => false);

        Assert.True(result.Found, result.Reason);
        Assert.Equal("Column", result.ConfirmedType);
        Assert.Equal("Budget Rate", result.Table);
        Assert.Equal(["Budget Rate"], tree.Searches);
        Assert.Equal(["1. Facts"], tree.Opened);
        Assert.Equal([2, 6, 7], tree.Selects);
        Assert.Equal(3, result.Tried);
        Assert.True(result.ByTable);
    }

    [Fact]
    public async Task AnObjectInAFolderTheFilterAlreadyOpenedIsFoundWithNoOpen()
    {
        // te3b-26: the filter showed 2. Other Currency Fields (its Composite Key column matched) but not Rate, four
        // rows further down the same folder in the cleared tree.
        var tree = new SearchedTree(Live, "Rate", ["Exchange Rate", "2. Other Currency Fields"]);

        var result = await ObjectSelect.ByTable(tree, new WalkRequest("Exchange Rate", "Rate", "Column"), () => false);

        Assert.True(result.Found, result.Reason);
        Assert.Equal("Exchange Rate", result.Table);
        Assert.Empty(tree.Opened);
        Assert.Equal([2, 12], tree.Selects);
    }

    [Fact]
    public async Task ACollapsedTableIsOpenedAndWithAFolderPathOnlyItsFoldersAre()
    {
        var tree = new SearchedTree(Live, "Rate");

        var result = await ObjectSelect.ByTable(
            tree,
            new WalkRequest("Budget Rate", "Rate", "Column", "1. Facts"),
            () => false
        );

        Assert.True(result.Found, result.Reason);
        Assert.Equal(["Budget Rate", "1. Facts"], tree.Opened);
        Assert.Equal([2, 5, 6], tree.Selects);
    }

    [Fact]
    public async Task AFolderPathDoesNotExpandARowTe3TypesAsAColumn()
    {
        static IReadOnlyList<(string, int?, string)> Rows(string search, IReadOnlySet<string> open)
        {
            List<(string, int?, string)> rows = [SpaceParts, Tables, ("Budget Rate", 2, "Table")];
            if (search == "" && open.Contains("Budget Rate"))
            {
                rows.Add(("1. Facts", 3, "Column"));
                rows.Add(("Other", 3, "Folder"));
                rows.Add(("Nested", 4, "Folder"));
                if (open.Contains("Nested"))
                {
                    rows.Add(("Rate", 5, "Column"));
                }
            }

            return rows;
        }

        var tree = new SearchedTree(Rows, "Rate");
        var result = await ObjectSelect.ByTable(
            tree,
            new WalkRequest("Budget Rate", "Rate", "Column", @"1. Facts\Nested"),
            () => false
        );

        Assert.False(result.Found);
        Assert.DoesNotContain("1. Facts", tree.Opened);
        Assert.DoesNotContain("Nested", tree.Opened);
    }

    // A calc group (te3b-28): TE3's filter shows nothing for the hyphenated name; its first token shows the table.
    private static IReadOnlyList<(string, int?, string)> CalcGroup(string search, IReadOnlySet<string> open)
    {
        List<(string, int?, string)> rows = [SpaceParts, Tables, ("Z01CG1 - Quantity", 2, "Table")];
        if (search == "" && open.Contains("Z01CG1 - Quantity"))
        {
            rows.AddRange([("Measure", 3, "Column"), ("Ordinal", 3, "Column")]);
        }

        return search is "Z01CG1" or "" ? rows : [];
    }

    [Fact]
    public async Task ATableNameTheFilterDoesNotMatchWholeIsSearchedByItsFirstToken()
    {
        var tree = new SearchedTree(CalcGroup, "Measure");

        var result = await ObjectSelect.ByTable(
            tree,
            new WalkRequest("Z01CG1 - Quantity", "Measure", "Column"),
            () => false
        );

        Assert.True(result.Found, result.Reason);
        Assert.Equal(["Z01CG1 - Quantity", "Z01CG1"], tree.Searches);
        Assert.Equal(["Z01CG1 - Quantity"], tree.Opened);
        Assert.Equal([2, 3], tree.Selects);
    }

    [Fact]
    public async Task ATableTheSearchDoesNotShowIsAMissThatFallsBackWithNoSelect()
    {
        var tree = new SearchedTree((_, _) => [Tables, ("Products", 2, "Table")], "Rate");

        var result = await ObjectSelect.ByTable(tree, new WalkRequest("Budget Rate", "Rate", "Column"), () => false);

        Assert.False(result.Found);
        Assert.True(result.FallBack);
        Assert.Contains("'Budget Rate' then 'Budget'", result.Reason);
        Assert.Equal(["Budget Rate", "Budget"], tree.Searches);
        Assert.Empty(tree.Selects);
        Assert.Empty(tree.Opened);
    }

    // A table with more empty folders than the walk may open.
    private static IReadOnlyList<(string, int?, string)> ManyFolders(string search, IReadOnlySet<string> open)
    {
        List<(string, int?, string)> rows = [SpaceParts, Tables, ("Budget Rate", 2, "Table")];
        if (search == "" && open.Contains("Budget Rate"))
        {
            rows.AddRange(Enumerable.Range(1, 6).Select(i => ($"Folder {i}", (int?)3, "Folder")));
        }

        rows.Add(("Customers", 2, "Table"));
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

    // A column named like its table ("Date" in Date): the name search and the table search type the same text; the
    // fake tells them apart by the first one's casing, and gives the name search no measurable indent.
    private static IReadOnlyList<(string, int?, string)> DateInDate(string search, IReadOnlySet<string> open)
    {
        List<(string, int?, string)> rows = [SpaceParts, Tables, ("Date", 2, "Table")];
        if (search == "" && open.Contains("Date"))
        {
            rows.Add(("Date", 3, "Column"));
        }

        return search is "Date" or "" ? rows : [("Date", null, "Table"), ("Date", null, "Column")];
    }

    [Fact]
    public async Task AnObjectNamedLikeItsTableIsStillTriedUnderTheTableWhenTheNameReadPlacedNoRow()
    {
        // Review (L3): with name == table the second try was skipped, and a name read that placed no row (0 rows)
        // then ran nothing at all. The strategy as the walk runs it tries the table, opens its row and finds the
        // column under it.
        var tree = new SearchedTree(DateInDate, "date");
        var steps = new List<string>();

        var result = await ObjectSelect.RowsThenTable(
            tree,
            new WalkRequest("Date", "Date", "Column"),
            () => false,
            null,
            steps.Add
        );

        Assert.True(result.Found, result.Reason);
        Assert.True(result.ByTable);
        Assert.Equal(["Date"], tree.Searches);
        Assert.Equal(["Date"], tree.Opened);
        Assert.Equal([2, 3], tree.Selects);
        Assert.Contains(steps, step => step.Contains("filtering by the table's name"));
    }

    [Fact]
    public async Task WhenTheTableTryMissesTooTheNameIsSearchedAgainOnlyForAKeyboardWalkThatFollows()
    {
        // A tree that scrolls makes the name read fall back with rows read (a keyboard walk follows, so the tree it
        // walks is searched again); a name read that placed no row leaves the table's filter as it is.
        var unplaced = new SearchedTree((_, _) => [Tables, ("Brands", 2, "Table"), ("Products", 2, "Table")], "Brand")
        {
            Complete = false,
        };
        var empty = new SearchedTree((_, _) => [("Brand", null, "Column")], "Brand");
        var request = new WalkRequest("Brands", "Brand", "Column");

        var walked = await ObjectSelect.RowsThenTable(unplaced, request, () => false, null, null);
        var missed = await ObjectSelect.RowsThenTable(empty, request, () => false, null, null);

        Assert.True(ObjectSelect.NeedsKeys(walked));
        Assert.Equal(["Brands", "Brand"], unplaced.Searches);
        Assert.False(ObjectSelect.NeedsKeys(missed));
        Assert.Equal(["Brands"], empty.Searches);
        Assert.Contains("Brands", missed.Reason);
    }
}
