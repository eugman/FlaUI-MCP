using System.Text.Json;
using FlaUI.Mcp;
using FlaUI.Mcp.Core;

/// <summary>
/// te3_navigate object's row strategy on the live TOM Explorer: one read of every realized row of the filtered tree,
/// one guarded select of the picked TreeItem, and Properties to confirm (see <see cref="ObjectSelect"/>).
/// </summary>
public sealed partial class Te3Page
{
    /// <summary>A navigation's reply with its proof (see <see cref="NavigationProof"/>).</summary>
    internal Task<McpToolResult> WithArrivalProof(McpToolResult reply, string destination, JsonElement a) =>
        NavigationProof.Attach(reply, this, destination, a);

    /// <summary>The proof of an object navigation: the selection TE3 names now against the request.</summary>
    internal async Task<FlaUI.Mcp.Core.Proof> ObjectProof(string table, string name, string? type) =>
        NavigationProof.Object(
            table,
            name,
            type,
            await Task.Run(ViewSelection),
            (await PlaceOf(lastRequest ?? new WalkRequest(table, name, type ?? ""))).Table
        );

    /// <summary>
    /// Where the rows in view place the selection now, read again rather than taken from the walk: the walk's
    /// indent (the tree's step does not change when the filter clears), else one measured in place.
    /// </summary>
    internal async Task<SelectionPlace> PlaceOf(WalkRequest request)
    {
        var found = await ReadTree();
        var scale = depthScale ?? TomRows.Calibrate(found.Elements);
        if (scale == null || RowTree.SelectedIndex(found) is not { } selected)
        {
            return new(null, false);
        }

        var rows = TomRows.Rows(found.Elements, scale);
        return new(
            SearchPick.TableOf(rows, selected),
            SearchPick.Candidates(rows, request).Candidates.Any(c => c.Index == selected)
        );
    }

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
    private async Task SelectRow(TreeRow row, Func<bool> outOfTime)
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
            await KeyBudget.Press(outOfTime, () => Keys("Home", TomExplorerTarget()), "Home to bring the row back");
            for (var page = 0; page < row.Page; page++)
            {
                await KeyBudget.Press(
                    outOfTime,
                    () => Keys("PageDown", TomExplorerTarget()),
                    $"PageDown {page + 1} of {row.Page} to bring the row back"
                );
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

    private const int RenderBudgetMs = 1500;
    private const int RenderPollMs = 250;

    // PageDown presses at most, to realize a filtered tree longer than the viewport (about 23 rows at 1080 px).
    private const int MaxPages = 6;

    private sealed class RowTree(Te3Page page, Func<bool> outOfTime) : IRowTree
    {
        // Home first, so the model root and its first child are in view to measure the indent whatever was selected
        // before; then every realized row, paging down while the tree scrolls (treeList has no Scroll pattern).
        public async Task<RowRead> Read()
        {
            await KeyBudget.Press(outOfTime, () => page.Keys("Home", TomExplorerTarget()), "Home before the row read");
            // TE3 applies the filter a moment after the search text: wait for the rows to stop changing.
            await RowSettle.Wait(
                async () => (await page.ReadTree()).Elements.Count(e => e.ControlType == "TreeItem"),
                ms => Task.Delay(ms),
                RenderBudgetMs,
                RenderPollMs
            );
            var truncated = false;
            var paged = await RowPager.ReadAll(
                async () =>
                {
                    var found = await page.ReadTree();
                    truncated |= found.Truncated;
                    page.depthScale ??= TomRows.Calibrate(found.Elements);
                    return (TomRows.Rows(found.Elements, page.depthScale), TomRows.Scrolls(found.Elements));
                },
                () => KeyBudget.Press(outOfTime, () => page.Keys("PageDown", TomExplorerTarget()), "PageDown"),
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

        // One read with no key: the indent is measured from what is in view, kept local so a view scrolled away from
        // the root never sets the walk's scale.
        public async Task<(IReadOnlyList<TreeRow>? Rows, int? Selected)> ReadInPlace()
        {
            var found = await page.ReadTree();
            var scale = TomRows.Calibrate(found.Elements);
            return (scale == null ? null : TomRows.Rows(found.Elements, scale), SelectedIndex(found));
        }

        internal static int? SelectedIndex(QueryResult found)
        {
            var selected = found.Elements.FirstOrDefault(e => e.ControlType == "TreeItem" && e.Selected == true);
            return
                selected?.Name is { } name
                && name.StartsWith("Node", StringComparison.Ordinal)
                && int.TryParse(name[4..], out var n)
                ? n
                : null;
        }

        public async Task<(IReadOnlyList<TreeRow>? Rows, int? Selected)> ReadSelected()
        {
            var found = await page.ReadTree();
            return (
                page.depthScale == null ? null : TomRows.Rows(found.Elements, page.depthScale),
                SelectedIndex(found)
            );
        }

        public Task Select(TreeRow row) => page.SelectRow(row, outOfTime);

        // A new filter: the indent is measured again from the rows it shows.
        public async Task Search(string text)
        {
            await KeyBudget.Press(outOfTime, () => page.SearchTomTyped(text), $"the search for '{text}'");
            page.depthScale = null;
        }

        public async Task Expand()
        {
            await KeyBudget.Press(outOfTime, () => page.Keys("Right", TomExplorerTarget()), "Right to open the table");
            await Task.Delay(WalkClock.CollapseSettleMs);
        }

        // A miss's text, with one read of where the selection ended and its table from the rows above it (the paged
        // rows are not kept here, so a table row above the screen is not known and the text says so).
        public async Task<string> Miss(WalkRequest request, string? reason, int inputs)
        {
            TreeRow? row = null;
            string? table = null;
            try
            {
                // After a keyboard walk the walk's indent may be unmeasured: measure it from the rows in view, so the
                // nearest table row above the selection names its table.
                var (rows, selected) = page.depthScale == null ? await ReadInPlace() : await ReadSelected();
                row = rows?.FirstOrDefault(r => r.Index == selected);
                table = row == null || rows == null ? null : SearchPick.TableOf(rows, row.Index);
            }
            catch (Exception error) when (error is InvalidOperationException or AutomationRefusal or TimeoutException)
            {
                reason += " (reading where the selection ended failed: " + error.Message + ")";
            }

            return WalkMiss.Text(request, reason, row, table, inputs);
        }

        public Task<SelectionView> View() => Task.Run(page.ViewSelection);

        public Task Wait(int ms) => Task.Delay(ms);
    }
}
