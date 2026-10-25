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
    internal async Task<McpToolResult> WithArrivalProof(McpToolResult reply, string destination, JsonElement a) =>
        WalkAccount.Attach(
            await NavigationProof.Attach(reply, this, destination, a),
            destination == "object" ? WalkReport : null
        );

    /// <summary>
    /// The proof of an object navigation: the selection TE3 names now against the request, under the table the rows
    /// in view place it (else, its table row scrolled above the viewport, the one the rows read before the clear
    /// placed it under; with neither, the proof is unavailable).
    /// </summary>
    internal async Task<FlaUI.Mcp.Core.Proof> ObjectProof(string table, string name, string? type) =>
        NavigationProof.Object(
            table,
            name,
            type,
            await Task.Run(ViewSelection),
            (await PlaceOf(lastRequest ?? new WalkRequest(table, name, type ?? ""))).Table ?? lastWalk?.Table
        );

    /// <summary>
    /// Where the rows in view place the selection now, read again rather than taken from the walk: the walk's
    /// indent (the tree's step does not change when the filter clears), else one measured in place.
    /// </summary>
    internal async Task<SelectionPlace> PlaceOf(WalkRequest request)
    {
        var found = await ReadTree();
        var scale = TomRows.FilteredScale(found.Elements, depthScale);
        if (scale == null || RowTree.SelectedIndex(found) is not { } selected)
        {
            return new(null, false);
        }

        return SearchPick.Place(TomRows.Rows(found.Elements, scale), selected, request);
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
            await KeyBudget.Press(
                outOfTime,
                () => Keys(TomRows.FirstNodeChord, TomExplorerTarget()),
                "first TOM Explorer node to bring the row back"
            );
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

    // Ctrl+Home scrolls/focuses the first DevExpress TreeList node but deliberately preserves the old selection.
    // The keyboard walkers read selection, so explicitly select and verify Node0 before they begin.
    private async Task SelectFirstTomRow(Func<bool>? outOfTime = null)
    {
        await KeyBudget.Press(
            outOfTime,
            () => Keys(TomRows.FirstNodeChord, TomExplorerTarget()),
            "first TOM Explorer node"
        );
        var reference = await Reference(FirstTomRowTarget());
        await KeyBudget.Press(
            outOfTime,
            () => invoke("windows_click", new { @ref = reference, action = "select" }),
            "select first TOM Explorer node"
        );
        if (RowTree.SelectedIndex(await ReadTree()) != 0)
        {
            throw new InvalidOperationException(
                "First TOM Explorer row did not become selected; no keyboard walk started."
            );
        }
    }

    internal static Target FirstTomRowTarget() =>
        new(new(Name: "Node0", ControlType: "TreeItem"), TomExplorerTarget().Selector, false);

    // The raw readings of the selection: the status bar's slot and the Properties rows (Name, ObjectType, the
    // display folder).
    private SelectionView ViewSelection() =>
        new(
            Te3StatusBar.SelectionSlot(ObserveModelState().Slots),
            PropertiesValue("Name"),
            PropertiesValue("ObjectType"),
            PropertiesFolder()
        );

    // Properties' Display Folder row: a column's grid names its DataItems after the labels, spaces included (Data
    // Type, Display Folder, Format String, ...; verified live on te3-2ac1444, 2026-10-22).
    private string? PropertiesFolder() => PropertiesValue("Display Folder");

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
            await KeyBudget.Press(
                outOfTime,
                () => page.Keys(TomRows.FirstNodeChord, TomExplorerTarget()),
                "first TOM Explorer node before the row read"
            );
            await Rendered();
            var truncated = false;
            var paged = await RowPager.ReadAll(
                async () =>
                {
                    var found = await page.ReadTree();
                    truncated |= found.Truncated;
                    page.depthScale = TomRows.FilteredScale(found.Elements, page.depthScale);
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

        // TE3 applies the filter a moment after the search text: wait for the rows to stop changing.
        private async Task Rendered() =>
            await RowSettle.Wait(
                async () => (await page.ReadTree()).Elements.Count(e => e.ControlType == "TreeItem"),
                ms => Task.Delay(ms),
                RenderBudgetMs,
                RenderPollMs
            );

        // One read with no key, once the filter has rendered: the indent is measured from what is in view (the rows
        // have no depth when the root is scrolled out), kept local so such a view never sets the walk's scale.
        public async Task<(IReadOnlyList<TreeRow>? Rows, int? Selected)> ReadInPlace()
        {
            await Rendered();
            var found = await page.ReadTree();
            return (
                TomRows.Rows(found.Elements, TomRows.FilteredScale(found.Elements, page.depthScale)),
                SelectedIndex(found)
            );
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

        // A new filter may hide the root; retain the scale measured before the first filter in this navigation.
        public async Task Search(string text)
        {
            await KeyBudget.Press(
                outOfTime,
                () => page.SearchTomTyped(text, outOfTime: outOfTime),
                $"the search for '{text}'"
            );
        }

        public async Task Expand()
        {
            await KeyBudget.Press(outOfTime, () => page.Keys("Right", TomExplorerTarget()), "Right to open the table");
            await Task.Delay(WalkClock.CollapseSettleMs);
        }

        // The filter cleared; a fresh root measurement will replace the retained scale when visible.
        public async Task ClearSearch()
        {
            await KeyBudget.Press(outOfTime, page.ClearTomSearchTyped, "the search's clear");
        }

        private const int TableDepth = 2;

        // The rows on screen with the selected row as the anchor and no key sent, once the rows have settled. After
        // the search clears TE3 scrolls the selected table to the top of the viewport, so the rows below it are its
        // subtree and the root is out of view: the indent is then measured from the selected row (a table, depth 2)
        // and the row below it, its first child. One screen: a subtree running past it is reported incomplete.
        public async Task<(RowRead Read, int? Selected)> ReadBelow()
        {
            await Rendered();
            var found = await page.ReadTree();
            var selected = SelectedIndex(found);
            page.depthScale =
                TomRows.FilteredScale(found.Elements, page.depthScale)
                ?? (selected is { } index ? TomRows.CalibrateFrom(found.Elements, index, TableDepth) : null);
            var rows = page.depthScale == null ? null : TomRows.Rows(found.Elements, page.depthScale);
            var cut = TomRows.Scrolls(found.Elements) && !Ended(rows, selected);
            return (new(rows, !cut, page.depthScale?.Step, found.Truncated, 0, cut), selected);
        }

        // The anchor's subtree ended on screen: a row below it at table depth or above.
        private static bool Ended(IReadOnlyList<TreeRow>? rows, int? anchor) =>
            rows != null && anchor is { } index && rows.Any(r => r.Index > index && r.Depth <= TableDepth);

        // A miss's text, read before the search is cleared and finished after it: where the selection is then (the
        // walk's own Home and PageDowns leave it on another row than before the walk), against the row it ended on.
        public async Task<string> MissCleared(WalkRequest request, string? reason, int inputs)
        {
            var (text, endedOn) = await Miss(request, reason, inputs);
            await page.ClearTomSearchTyped();
            var bar = await Task.Run(() => Te3StatusBar.SelectionSlot(page.ObserveModelState().Slots));
            return text + WalkMiss.AfterClear(bar, endedOn);
        }

        // One read of where the selection ended and its table from the rows above it (the paged rows are not kept
        // here, so a table row above the screen is not known and the text says so), and the row it ended on.
        private async Task<(string Text, string? EndedOn)> Miss(WalkRequest request, string? reason, int inputs)
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

            return (WalkMiss.Text(request, reason, row, table, inputs), row?.Value);
        }

        public Task<SelectionView> View() => Task.Run(page.ViewSelection);

        public Task Wait(int ms) => Task.Delay(ms);
    }
}
