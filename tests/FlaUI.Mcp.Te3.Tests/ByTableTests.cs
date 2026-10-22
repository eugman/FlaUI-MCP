using Xunit;

/// <summary>
/// The row strategy's second try (vte3g walks 4 and 6): when the object's name shows no placed candidate, the tree is
/// filtered by the table's name, the table row is selected and expanded, and the object is looked for under it.
/// Scripted rows; no desktop access.
/// </summary>
public sealed class ByTableTests
{
    /// <summary>A filtered tree whose rows depend on the search text and on whether the table row was expanded.</summary>
    private sealed class SearchedTree(
        Func<string, bool, IReadOnlyList<(string Value, int? Depth, string Type)>> rowsFor,
        string firstSearch
    ) : IRowTree
    {
        private string search = firstSearch;
        private bool expanded;
        private TreeRow? selected;

        public List<string> Searches { get; } = [];
        public List<int> Selects { get; } = [];
        public int Expands { get; private set; }

        private IReadOnlyList<(string Value, int? Depth, string Type)> Now => rowsFor(search, expanded);

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
            (search, expanded, selected) = (text, false, null);
            return Task.CompletedTask;
        }

        public Task Expand()
        {
            Expands++;
            expanded = true;
            return Task.CompletedTask;
        }
    }

    // "Rate" matches Tax Rate, the calculation items and Budget Rate's column; the filtered view it gave live had no
    // measurable indent. Filtering by "Budget Rate" shows the table, and expanding it its columns.
    private static IReadOnlyList<(string, int?, string)> Rows(string search, bool expanded)
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
        if (expanded)
        {
            rows.AddRange([("1. Facts", 3, "Folder"), ("Rate", 4, "Column"), ("Currency", 3, "Column")]);
        }

        return rows;
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
        Assert.Equal(1, tree.Expands);
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
        Assert.Equal(0, tree.Expands);
    }
}
