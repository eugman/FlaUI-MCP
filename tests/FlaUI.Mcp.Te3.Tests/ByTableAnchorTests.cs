using Xunit;

/// <summary>Table anchors must survive clearing the filter and the fresh candidate read.</summary>
public sealed class ByTableAnchorTests
{
    private sealed class ChangingTable(bool collapsed, bool sameNamedFolder = false) : IRowTree
    {
        private bool cleared;
        private int? selected;
        public List<int> Selects { get; } = [];
        public int Expands { get; private set; }
        private string TableName => cleared && !sameNamedFolder ? "Other" : "Wanted";
        private IReadOnlyList<TreeRow> Rows =>
            cleared && collapsed
                ? [new(0, "Model", 0), new(1, "Tables", 1), new(2, "Other", 2)]
                : [new(0, "Model", 0), new(1, "Tables", 1), new(2, TableName, 2), new(3, "Amount", 3)];

        public Task<RowRead> Read() => Task.FromResult(new RowRead(Rows, true, 24, false));

        public Task<(IReadOnlyList<TreeRow>? Rows, int? Selected)> ReadSelected() =>
            Task.FromResult<(IReadOnlyList<TreeRow>?, int?)>((Rows, selected));

        public Task<(RowRead Read, int? Selected)> ReadBelow() =>
            Task.FromResult((new RowRead(Rows, true, 24, false), selected));

        public Task Select(TreeRow row)
        {
            Selects.Add(row.Index);
            selected = row.Index;
            return Task.CompletedTask;
        }

        public Task ClearSearch()
        {
            cleared = true;
            return Task.CompletedTask;
        }

        public Task Expand()
        {
            Expands++;
            return Task.CompletedTask;
        }

        public Task<SelectionView> View()
        {
            var row = Rows.FirstOrDefault(r => r.Index == selected);
            if (cleared && sameNamedFolder && row?.Depth == 2)
            {
                return Task.FromResult(new SelectionView("Table \"Wanted\"", "Wanted", "Folder", null));
            }

            var type = row?.Depth == 2 ? "Table" : "Column";
            return Task.FromResult(
                new SelectionView(row == null ? null : $"{type} \"{row.Value}\"", row?.Value, type, null)
            );
        }

        public Task Wait(int ms) => Task.CompletedTask;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClearingSearchMustNotSelectOrExpandAReplacementTable(bool collapsed)
    {
        var tree = new ChangingTable(collapsed);

        var result = await ObjectSelect.ByTable(tree, new WalkRequest("Wanted", "Amount", "Column"), () => false);

        Assert.False(result.Found);
        Assert.Equal([2], tree.Selects);
        Assert.Equal(0, tree.Expands);
    }

    [Fact]
    public async Task AStaleStatusBarCannotConfirmASameNamedFolderAsTheTable()
    {
        var tree = new ChangingTable(false, sameNamedFolder: true);

        var result = await ObjectSelect.ByTable(tree, new WalkRequest("Wanted", "Amount", "Column"), () => false);

        Assert.False(result.Found);
        Assert.Equal([2], tree.Selects);
        Assert.Equal(0, tree.Expands);
    }

    [Theory]
    [InlineData("Other", 2)]
    [InlineData("Wanted", 3)]
    [InlineData("Wanted", null)]
    public void AStaleAnchorIndexDoesNotOverrideTheFreshRowsNameOrDepth(string name, int? depth)
    {
        TreeRow[] rows = [new(2, name, depth), new(3, "Amount", 3)];

        var pick = SearchPick.Candidates(rows, new WalkRequest("Wanted", "Amount", "Column"), tableRow: 2);

        Assert.Empty(pick.Candidates);
    }
}
