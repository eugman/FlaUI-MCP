using FlaUI.Mcp.Core;

/// <summary>
/// te3_navigate object's row strategy on the live TOM Explorer: one read of every realized row of the filtered tree,
/// one guarded select of the picked TreeItem, and Properties to confirm (see <see cref="ObjectSelect"/>).
/// </summary>
public sealed partial class Te3Page
{
    private const int PropertiesPollMs = 300;
    private const int PropertiesReads = 3;

    /// <summary>
    /// The last object navigation's account for the reply: the strategy used, the rows read, the measured indent
    /// step in pixels, the selects sent and, after a keyboard fallback, the rows walked.
    /// </summary>
    public object? WalkReport { get; private set; }

    // Every element of the tree: the TreeItems (NodeN), their DataItems (Name row N) and the scroll bar.
    private async Task<QueryResult> ReadTree() =>
        await Task.Run(() =>
                query.Find(
                    handle,
                    new(),
                    TomExplorerTarget().Selector,
                    maxResults: 450,
                    register: false,
                    includeBounds: true,
                    budget: new SearchBudget(3000, TimeSpan.FromSeconds(3))
                )
            )
            .WaitAsync(TimeSpan.FromSeconds(5));

    // One guarded select of TreeItem NodeN (Core's windows_click action select; it clicks when Select is ignored).
    private async Task SelectRow(TreeRow row)
    {
        var reference = await Reference(
            new(new(Name: "Node" + row.Index, ControlType: "TreeItem"), TomExplorerTarget().Selector, false)
        );
        reportStep?.Invoke($"Select TOM Explorer row {row.Index} '{row.Value}'");
        await invoke("windows_click", new { @ref = reference, action = "select" });
    }

    // TE3's account of the selection: the status bar's slot when it names the row, otherwise Properties.
    private Confirmation Confirm(TreeRow row)
    {
        var bar = Te3StatusBar.SelectionSlot(ObserveModelState().Slots);
        var folder = PropertiesValue("Folder");
        return TomSelection.SelectionName(bar) == row.Value
            ? new(row.Value, TomSelection.SelectionType(bar), folder)
            : new(PropertiesValue("Name"), PropertiesValue("ObjectType"), folder);
    }

    private sealed class RowTree(Te3Page page) : IRowTree
    {
        public async Task<RowRead> Read()
        {
            var found = await page.ReadTree();
            page.depthScale ??= TomRows.Calibrate(found.Elements);
            var scale = page.depthScale;
            var rows = scale == null ? null : TomRows.Rows(found.Elements, scale);
            return new(rows, !TomRows.Scrolls(found.Elements), scale?.Step, found.Truncated);
        }

        public async Task<(IReadOnlyList<TreeRow>? Rows, int? Selected)> ReadSelected()
        {
            var found = await page.ReadTree();
            var selected = found.Elements.FirstOrDefault(e => e.ControlType == "TreeItem" && e.Selected == true);
            var index =
                selected?.Name is { } name
                && name.StartsWith("Node", StringComparison.Ordinal)
                && int.TryParse(name[4..], out var n)
                    ? n
                    : (int?)null;
            return (page.depthScale == null ? null : TomRows.Rows(found.Elements, page.depthScale), index);
        }

        public Task Select(TreeRow row) => page.SelectRow(row);

        // The status bar names an object selection at once; Properties follows a moment later and has no ObjectType
        // row for a column. A few short reads, never a key.
        public async Task<Confirmation> Selected(TreeRow row)
        {
            var seen = new Confirmation(null, null, null);
            for (var attempt = 0; attempt < PropertiesReads && seen.Name == null; attempt++)
            {
                await Task.Delay(PropertiesPollMs);
                seen = await Task.Run(() => page.Confirm(row));
            }

            return seen;
        }
    }
}
