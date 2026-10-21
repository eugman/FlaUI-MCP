using FlaUI.Mcp.Core;

/// <summary>
/// te3_navigate object's row strategy on the live TOM Explorer: one read of every realized row of the filtered tree,
/// one guarded select of the picked TreeItem, and Properties to confirm (see <see cref="ObjectSelect"/>).
/// </summary>
public sealed partial class Te3Page
{
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

    // One guarded select of TreeItem NodeN (Core's windows_click action select; it clicks when Select is ignored). A
    // row read on a later page is no longer realized once the tree paged on: Home and the same PageDowns bring it back.
    private async Task SelectRow(TreeRow row)
    {
        var target = new Target(
            new(Name: "Node" + row.Index, ControlType: "TreeItem"),
            TomExplorerTarget().Selector,
            false
        );
        string reference;
        try
        {
            reference = await Reference(target);
        }
        catch (ElementNotFoundException)
        {
            await Keys("Home", TomExplorerTarget());
            for (var page = 0; page < row.Page; page++)
            {
                await Keys("PageDown", TomExplorerTarget());
            }

            reference = await Reference(target);
        }

        reportStep?.Invoke($"Select TOM Explorer row {row.Index} '{row.Value}'");
        await invoke("windows_click", new { @ref = reference, action = "select" });
    }

    // The raw readings of the selection: the status bar's slot and the Properties rows (Name, ObjectType, Folder).
    private SelectionView ViewSelection() =>
        new(
            Te3StatusBar.SelectionSlot(ObserveModelState().Slots),
            PropertiesValue("Name"),
            PropertiesValue("ObjectType"),
            PropertiesValue("Folder")
        );

    // PageDown presses at most, to realize a filtered tree longer than the viewport (about 23 rows at 1080 px).
    private const int MaxPages = 6;

    private sealed class RowTree(Te3Page page, Func<bool> outOfTime) : IRowTree
    {
        // Home first, so the model root and its first child are in view to measure the indent whatever was selected
        // before; then every realized row, paging down while the tree scrolls (treeList has no Scroll pattern).
        public async Task<RowRead> Read()
        {
            await page.Keys("Home", TomExplorerTarget());
            var truncated = false;
            var paged = await RowPager.ReadAll(
                async () =>
                {
                    var found = await page.ReadTree();
                    truncated |= found.Truncated;
                    page.depthScale ??= TomRows.Calibrate(found.Elements);
                    return (TomRows.Rows(found.Elements, page.depthScale), TomRows.Scrolls(found.Elements));
                },
                () => page.Keys("PageDown", TomExplorerTarget()),
                MaxPages,
                outOfTime
            );
            return new(
                page.depthScale == null ? null : paged.Rows,
                paged.Complete,
                page.depthScale?.Step,
                truncated,
                paged.Pages,
                paged.TruncatedByViewport
            );
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

        public Task<SelectionView> View() => Task.Run(page.ViewSelection);

        public Task Wait(int ms) => Task.Delay(ms);
    }
}
