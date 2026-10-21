using FlaUI.Mcp.Core;

/// <summary>The TOM Explorer object walk behind te3_navigate object: search, walk the filtered tree, clear, check.</summary>
public sealed partial class Te3Page
{
    /// <summary>
    /// Selects an object in TOM Explorer by name, type and table. The object name is typed into the search box, then the
    /// selection walks down the filtered tree (see <see cref="PathWalker"/>), so table groups, display folders and rows
    /// that share a name (a Date table, a Date column, a Date level) need no path. With <paramref name="folder"/> the
    /// object must be inside that display folder, and the walk stops once it leaves the table (see
    /// <see cref="WalkPlace"/>). The search is cleared afterwards and the selection is checked again.
    /// </summary>
    public async Task SelectObjectPath(string table, string objectName, string objectType, string folder = "")
    {
        if (objectType is not ("" or "Table" or "Column" or "Measure"))
        {
            throw new ArgumentException("Unsupported object type");
        }

        if (folder.Length > 0 && objectType == "Table")
        {
            throw new ArgumentException("folder applies to columns and measures; a table is in no display folder.");
        }

        // The walk's time limit counts from here: opening TOM Explorer, the gate and the search use the same 30 s.
        var elapsed = WalkClock.Start();
        await NavigationRetry.Once(
            () => WalkToObject(table, objectName, objectType, folder, elapsed),
            () => Inputs.Sent,
            reportStep
        );
    }

    // The TOM Explorer label indent, measured during the walk; null until the root and its first child were read.
    private TomRows.Scale? depthScale;

    // Where the last walk ended and what it was asked for; the arrival check compares them with the selection.
    private WalkResult? lastWalk;
    private WalkRequest? lastRequest;

    private async Task WalkToObject(
        string table,
        string objectName,
        string objectType,
        string folder,
        Func<TimeSpan> elapsed
    )
    {
        await OpenTom();
        if (await ShowChangesGate.TurnOff(TomChangesState, () => Click(TomChangesTarget())))
        {
            reportStep?.Invoke("Turned TOM Explorer Show changes off before walking the tree");
        }

        await SearchTomTyped(objectName);
        depthScale = null;
        var request = new WalkRequest(table, objectName, objectType, folder);
        var cursor = new TomCursor(this, ObjectWalk.Interesting(request));
        var walk = await FindObject(request, cursor, elapsed);
        if (!walk.Found)
        {
            await ClearTomSearchTyped();
            throw new InvalidOperationException(
                $"No {objectType} '{objectName}' under table '{table}'{(folder.Length > 0 ? $" in folder '{folder}'" : "")} "
                    + $"in TOM Explorer ({DescribeWalk()}; {Inputs.Sent} inputs). {walk.Reason}"
            );
        }

        reportStep?.Invoke($"Found {objectType} '{objectName}' under '{walk.Table}' after {walk.RowsVisited} rows");
        await ClearTomSearchTyped();
        // Clearing the filter keeps the selection; check it really did before reporting arrival.
        if (!await ObjectWalk.Arrived(cursor, walk, request, () => PropertiesValue("Name")))
        {
            var after = await cursor.Current();
            throw new InvalidOperationException(
                $"Selection moved when the search was cleared: now '{after.Value}' ({after.Type ?? "unknown type"})."
            );
        }

        lastWalk = walk;
        lastRequest = request;
    }

    // The row strategy first: one read of the filtered rows, one select, Properties to confirm. The keyboard walk runs
    // only when the rows cannot be placed or may not all be realized, and the 18 s stop bounds both.
    private async Task<WalkResult> FindObject(WalkRequest request, TomCursor cursor, Func<TimeSpan> elapsed)
    {
        var picked = await ObjectSelect.Run(new RowTree(this), request, WalkClock.OutOfTime(elapsed));
        WalkReport = new
        {
            strategy = "rows",
            objectType = picked.ConfirmedType,
            rowsRead = picked.RowsRead,
            indentPx = picked.IndentPx,
            selects = picked.Tried,
            truncated = picked.Truncated,
            rejected = picked.Rejected,
        };
        if (!picked.FallBack)
        {
            return new(picked.Found, picked.Found ? request.Table : null, picked.RowsRead, picked.Reason);
        }

        reportStep?.Invoke($"Row read could not place the object ({picked.Reason}); walking the tree with keys");
        await Keys("Home", TomExplorerTarget());
        var walk = await ObjectWalk.Find(
            cursor,
            request,
            WalkClock.OutOfTime(elapsed),
            WalkClock.CanRead(elapsed),
            () => PropertiesValue("Name")
        );
        WalkReport = new
        {
            strategy = "keys",
            rowsRead = picked.RowsRead,
            indentPx = picked.IndentPx,
            selects = picked.Tried,
            truncated = picked.Truncated,
            rejected = picked.Rejected,
            rowsWalked = walk.RowsVisited,
        };
        return walk;
    }

    private string DescribeWalk() => System.Text.Json.JsonSerializer.Serialize(WalkReport);

    /// <summary>te3_navigate object's arrival check: the last walk's object, in its folder, is still selected.</summary>
    private async Task VerifyObjectArrival()
    {
        if (
            lastWalk == null
            || lastRequest == null
            || !await ObjectWalk.Arrived(new TomCursor(this), lastWalk, lastRequest, () => PropertiesValue("Name"))
        )
        {
            throw new InvalidOperationException(
                "Object identity unverified: the selection is not the object the walk reached in its table and folder. "
                    + "No fallback guess was made."
            );
        }
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
    // Each step's cost is its reads (about 0.5 s each), so the cursor keeps the last settled read and reuses it as the
    // next key's "before", and reads Properties only for a row that could matter to the walk.
    private sealed class TomCursor(Te3Page page, Func<string?, bool>? interesting = null) : ITreeCursor
    {
        private (SelectionSnapshot Snapshot, bool RowBelow, bool ChildBelow)? last;

        public async Task<RowObservation> Current() => await Typed((await Observe()).Row);

        public bool Stalled { get; private set; }

        public async Task<RowObservation?> Down()
        {
            var (row, rowBelow) = await Move("Down");
            Stalled = row == null && rowBelow;
            return row;
        }

        /// <summary>
        /// Press Left on the selected row, only when the row below it is deeper (it is open). True once the selection
        /// read back unchanged: Left on a collapsed row would move to its parent instead, and then nothing is assumed.
        /// </summary>
        public async Task<bool> Collapse()
        {
            var before = last ?? await Read();
            if (!before.ChildBelow)
            {
                return false;
            }

            await page.Keys("Left", TomExplorerTarget());
            await Task.Delay(WalkClock.CollapseSettleMs);
            return (await Observe()).Row == before.Snapshot.Row;
        }

        /// <summary>Press Up and return the row above once it has settled; null at the top.</summary>
        public async Task<RowObservation?> Up() => (await Move("Up")).Row;

        // The row the key moved to, and whether a row was shown below the selection before the key.
        private async Task<(RowObservation? Row, bool RowBelow)> Move(string key)
        {
            var before = last ?? await Read();
            await page.Keys(key, TomExplorerTarget());
            var moved = await WalkClock.AwaitMove(before.Snapshot, Observe, WalkClock.Start(), ms => Task.Delay(ms));
            return (moved == null ? null : await Typed(moved.Row), before.RowBelow);
        }

        private async Task<(SelectionSnapshot Snapshot, bool RowBelow, bool ChildBelow)> Read() =>
            (last = await page.ReadSelection()).Value;

        private async Task<SelectionSnapshot> Observe() => (await Read()).Snapshot;

        // Groups and the model root need no type; nor does a row whose text the walk is not looking for.
        private Task<RowObservation> Typed(RowObservation row) =>
            row.Depth <= 1 || interesting?.Invoke(row.Value) == false ? Task.FromResult(row) : page.Typed(row);
    }

    /// <summary>One observation of the selection: the row, plus the raw bar text and row position to detect movement.</summary>
    private sealed record SelectionSnapshot(RowObservation Row, string? BarText, double? RowTop);

    private async Task<SelectionSnapshot> ObserveSelection() => (await ReadSelection()).Snapshot;

    // The selection, and whether any realized row lies below it (kept out of the snapshot, which detects movement).
    // One read of the tree gives the selected TreeItem and its DataItem, whose indent is the row's depth; the status
    // bar's selection slot gives the type when it names this row (a selected display folder leaves the bar on the
    // previous object; Typed then reads Properties).
    // Also whether the row shown below is deeper (the selection is open, so Left collapses it).
    private async Task<(SelectionSnapshot Snapshot, bool RowBelow, bool ChildBelow)> ReadSelection()
    {
        var rows = await ReadTree();
        // The indent is measured once per walk, from the root and its first child (in view after Home).
        depthScale ??= TomRows.Calibrate(rows.Elements);
        var (selected, depth) = TomRows.Selected(rows.Elements, depthScale);
        var bar = await Task.Run(() => Te3StatusBar.SelectionSlot(ObserveModelState().Slots));
        var value = selected?.Value ?? TomSelection.SelectionName(bar);
        var type = TomRows.Type(value, bar, () => null);
        var below = TomSelection.RowShownBelow(
            rows.Elements.Where(r => r.ControlType == "TreeItem").Select(r => r.Bounds?.Y),
            selected?.Bounds?.Y
        );
        return (
            new(new RowObservation(value, type, depth), bar, selected?.Bounds?.Y),
            below,
            TomRows.ChildBelow(rows.Elements, depthScale)
        );
    }

    // The row with its type from Properties' ObjectType when the status bar did not name it: read once per settled row,
    // not on every poll while a move settles.
    private async Task<RowObservation> Typed(RowObservation row) =>
        row.Type != null ? row : row with { Type = await Task.Run(() => PropertiesValue("ObjectType")) };

    /// <summary>A value the Properties pane shows for the selection (a DataItem named after the property), or null.</summary>
    private string? PropertiesValue(string property)
    {
        try
        {
            var found = query.Find(
                handle,
                new(Name: property, ControlType: "DataItem"),
                new(AutomationId: "propertyGrid"),
                maxResults: 2,
                register: false,
                budget: new SearchBudget(1500, TimeSpan.FromSeconds(2))
            );
            return found.Elements.Count == 1 ? found.Elements[0].Value : null;
        }
        catch (ElementNotFoundException)
        {
            return null;
        }
    }
}
