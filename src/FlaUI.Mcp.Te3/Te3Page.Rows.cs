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

    private sealed class RowTree(Te3Page page) : IRowTree
    {
        public async Task<(IReadOnlyList<TreeRow>? Rows, bool Complete, double? IndentPx)> Read()
        {
            var found = await page.ReadTree();
            page.depthScale ??= TomRows.Calibrate(found.Elements);
            var scale = page.depthScale;
            var complete = !found.Truncated && !TomRows.Scrolls(found.Elements);
            return (scale == null ? null : TomRows.Rows(found.Elements, scale), complete, scale?.Step);
        }

        public Task Select(TreeRow row) => page.SelectRow(row);

        // Properties follows the selection a moment later; a few short reads, never a key.
        public async Task<(string? Name, string? Type)> Selected()
        {
            (string? Name, string? Type) read = (null, null);
            for (var attempt = 0; attempt < PropertiesReads && read.Name == null; attempt++)
            {
                await Task.Delay(PropertiesPollMs);
                read = await Task.Run(() => (page.PropertiesValue("Name"), page.PropertiesValue("ObjectType")));
            }

            return read;
        }
    }
}
