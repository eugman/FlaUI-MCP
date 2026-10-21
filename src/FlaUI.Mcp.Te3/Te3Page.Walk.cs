using FlaUI.Mcp.Core;

/// <summary>The TOM Explorer object walk behind te3_navigate object: search, walk the filtered tree, clear, check.</summary>
public sealed partial class Te3Page
{
    /// <summary>
    /// Selects an object in TOM Explorer by name, type and table. The object name is typed into the search box, then the
    /// selection walks down the filtered tree (see <see cref="PathWalker"/>), so table groups, display folders and rows
    /// that share a name (a Date table, a Date column, a Date level) need no path. <paramref name="folder"/> is accepted
    /// for compatibility and not needed. The search is cleared afterwards and the selection is checked again.
    /// </summary>
    public async Task SelectObjectPath(string table, string objectName, string objectType, string folder = "")
    {
        if (objectType is not ("Table" or "Column" or "Measure"))
        {
            throw new ArgumentException("Unsupported object type");
        }

        _ = folder;
        // The walk's time limit counts from here: opening TOM Explorer, the gate and the search use the same 30 s.
        var elapsed = WalkClock.Start();
        await NavigationRetry.Once(
            () => WalkToObject(table, objectName, objectType, elapsed),
            () => Inputs.Sent,
            reportStep
        );
    }

    // Where the last walk ended; the arrival check compares it with the request.
    private WalkResult? lastWalk;

    private async Task WalkToObject(string table, string objectName, string objectType, Func<TimeSpan> elapsed)
    {
        await OpenTom();
        if (await ShowChangesGate.TurnOff(TomChangesState, () => Click(TomChangesTarget())))
        {
            reportStep?.Invoke("Turned TOM Explorer Show changes off before walking the tree");
        }

        await SearchTomTyped(objectName);
        await Keys("Home", TomExplorerTarget());
        var cursor = new TomCursor(this);
        var walk = await PathWalker.Find(
            cursor,
            table,
            objectName,
            objectType,
            outOfTime: WalkClock.OutOfTime(elapsed)
        );
        if (!walk.Found)
        {
            await ClearTomSearchTyped();
            throw new InvalidOperationException(
                $"No {objectType} '{objectName}' under table '{table}' in TOM Explorer after {walk.RowsVisited} rows "
                    + $"and {Inputs.Sent} inputs. {walk.Reason}"
            );
        }

        reportStep?.Invoke($"Found {objectType} '{objectName}' under '{walk.Table}' after {walk.RowsVisited} rows");
        await ClearTomSearchTyped();
        // Clearing the filter keeps the selection; check it really did before reporting arrival.
        var after = await cursor.Current();
        if (!PathWalker.Matches(after, walk.Table, table, objectName, objectType))
        {
            throw new InvalidOperationException(
                $"Selection moved when the search was cleared: now '{after.Value}' ({after.Type ?? "unknown type"})."
            );
        }

        lastWalk = walk;
    }

    /// <summary>Types into the TOM Explorer search box; setting its value does not apply TE3's filter.</summary>
    private async Task SearchTomTyped(string text)
    {
        await ClearTomSearchTyped();
        var reference = await Reference(SearchTarget());
        reportStep?.Invoke($"Type TOM Explorer search {text}");
        await invoke("windows_type", new { @ref = reference, text });
        await Task.Delay(800);
    }

    private async Task ClearTomSearchTyped()
    {
        var reference = await Reference(SearchTarget());
        reportStep?.Invoke("Clear TOM Explorer search");
        await invoke("windows_send_keys", new { @ref = reference, keys = new[] { "Ctrl+A", "Backspace" } });
        await Task.Delay(600);
    }

    /// <summary>
    /// The selected TOM Explorer row and the status bar's type for it; nothing is clicked. Each step reads every realized
    /// row and the status bar through the generic query: correct, but about 0.5 s a read, and a Down takes three or more
    /// reads (about 2 s live), so a long filtered tree can exceed the 30 s tool timeout (see <see cref="WalkClock"/>). A
    /// version that cached the tree element timed out live; speed this up with care.
    /// </summary>
    private sealed class TomCursor(Te3Page page) : ITreeCursor
    {
        public async Task<RowObservation> Current() => (await page.ObserveSelection()).Row;

        public Task<RowObservation?> Down() => Move("Down");

        /// <summary>Press Up and return the row above once it has settled; null at the top.</summary>
        public Task<RowObservation?> Up() => Move("Up");

        private async Task<RowObservation?> Move(string key)
        {
            var before = await page.ObserveSelection();
            await page.Keys(key, TomExplorerTarget());
            var moved = await WalkClock.AwaitMove(
                before,
                page.ObserveSelection,
                WalkClock.Start(),
                ms => Task.Delay(ms)
            );
            return moved?.Row;
        }
    }

    /// <summary>One observation of the selection: the row, plus the raw bar text and row position to detect movement.</summary>
    private sealed record SelectionSnapshot(RowObservation Row, string? BarText, double? RowTop);

    private async Task<SelectionSnapshot> ObserveSelection()
    {
        var tree = TomExplorerTarget();
        var rows = await Task.Run(() =>
                query.Find(
                    handle,
                    new(ControlType: "TreeItem"),
                    tree.Selector,
                    maxResults: 200,
                    register: false,
                    includeBounds: true,
                    budget: new SearchBudget(3000, TimeSpan.FromSeconds(3))
                )
            )
            .WaitAsync(TimeSpan.FromSeconds(5));
        var selected = rows.Elements.FirstOrDefault(r => r.Selected == true);
        var bar = await Task.Run(() => ObserveModelState().Selection);
        return new(TomSelection.ObservedRow(selected?.Value, bar), bar, selected?.Bounds?.Y);
    }

    public static string ObjectNotFoundMessage(string table, string objectName) =>
        $"'{objectName}' is not directly under table '{table}'. If it is in a display folder, pass folder (e.g. Smoke tests); "
        + "if it should be there, the model may have changed since it was loaded.";
}
