/// <summary>
/// The row strategy's second try: when the object's own name shows no placed candidate (live, "Rate" and "Measure"
/// match calculation items in many tables and the filtered view gave no measurable indent), the tree is filtered by
/// the table's name, the table row is selected and confirmed, and the filter is cleared. TE3's filter shows under the
/// table only rows whose names contain the search text (te3b-24/26: Rate under Budget Rate never showed, Exchange
/// Rate Composite Key did), while the cleared tree shows the whole subtree with the selected table scrolled to the
/// top of the viewport: the object is looked for in the rows below it, its display folders opened in turn.
/// </summary>
internal static partial class ObjectSelect
{
    /// <summary>Rows opened under the table at most; the 18 s clock bounds them too.</summary>
    public const int MaxFolderOpens = 4;

    /// <summary>Rows selected under the table at most, to tell its folders from its objects (each costs a select).</summary>
    public const int MaxProbes = 8;

    private const int TableDepth = 2;

    private const string PartitionsGroup = "Partitions";

    private const int ProbeReads = 3;

    /// <summary>The selected table row the rows below are placed under, and its name as TE3 confirmed it.</summary>
    private sealed record Anchor(int Index, string Table);

    /// <summary>
    /// The row strategy as te3_navigate object runs it: the name search's rows first and, whenever they did not
    /// settle it (an object named like its table included: the same search, but with the table row opened), the
    /// table's name. When that misses too, the object's name is searched again only when a keyboard walk follows
    /// (<see cref="NeedsKeys"/>), so the walk sees the tree it always walked.
    /// </summary>
    public static async Task<SelectResult> RowsThenTable(
        IRowTree tree,
        WalkRequest request,
        Func<bool> outOfTime,
        WalkRequest? lastArrived,
        Action<string>? report
    )
    {
        var first = await Run(tree, request, outOfTime, lastArrived);
        // A complete filtered view can still omit children of collapsed folders. When no row
        // was placed, try the table anchor even though paging found the end of that view.
        if (first.Found || (!first.FallBack && first.Tried > 0))
        {
            return first;
        }

        report?.Invoke($"Row read could not place the object ({first.Reason}); filtering by the table's name");
        var byTable = await ByTable(tree, request, outOfTime);
        if (byTable.Found || !byTable.FallBack)
        {
            return byTable;
        }

        if (NeedsKeys(first))
        {
            await tree.Search(request.ObjectName);
        }

        return first with
        {
            Reason = $"{first.Reason} {byTable.Reason}",
        };
    }

    public static async Task<SelectResult> ByTable(IRowTree tree, WalkRequest request, Func<bool> outOfTime)
    {
        var (read, table, searches) = await FindTable(tree, request.Table);
        if (table == null)
        {
            var tried = string.Join(" then ", searches.Select(s => $"'{s}'"));
            return new(
                false,
                null,
                read.Rows?.Count ?? 0,
                read.IndentPx,
                0,
                $"Searching for the table's own name ({tried}) showed no row placed as that table.",
                true,
                read.Truncated
            );
        }

        if ((await tree.ReadSelected()).Selected != table.Index)
        {
            await tree.Select(table);
        }

        var seen = await Settled(tree, table, new WalkRequest(request.Table, request.Table, "Table"));
        if (seen == null || !TomSelection.ObjectTypeMatches("Table", seen.Type))
        {
            var named = seen == null ? "nothing" : $"{seen.Type ?? "no type"} '{seen.Name}'";
            var why =
                $"The row selected for table '{request.Table}' was not named as that table by TE3 (it reads {named}).";
            return new(false, table, read.Rows!.Count, read.IndentPx, 1, why, false, read.Truncated);
        }

        await tree.ClearSearch();
        var (below, selected) = await tree.ReadBelow();
        if (selected is not { } index || !await IsTableAnchor(tree, below, index, request.Table))
        {
            var why = $"Clearing the search lost the confirmed selection on table '{request.Table}'.";
            return new(false, table, below.Rows?.Count ?? 0, below.IndentPx, 1, why, false, below.Truncated);
        }

        if (!ChildBelow(below.Rows, index))
        {
            await tree.Expand();
            (below, selected) = await tree.ReadBelow();
            if (selected != index || !await IsTableAnchor(tree, below, index, request.Table))
            {
                return new(
                    false,
                    table,
                    below.Rows?.Count ?? 0,
                    below.IndentPx,
                    1,
                    "Expanding the table lost its confirmed selection.",
                    false,
                    below.Truncated
                );
            }
        }

        return await UnderTable(tree, request, outOfTime, below, new Anchor(index, request.Table));
    }

    // The table's row in the tree filtered by its name. TE3's filter may not match a whole hyphenated name (te3b-28,
    // "Z01CG1 - Quantity"): its first token is searched next, and the row is still confirmed by its full text.
    private static async Task<bool> IsTableAnchor(IRowTree tree, RowRead read, int index, string tableName)
    {
        var row = read.Rows?.FirstOrDefault(r => r.Index == index);
        if (row?.Value != tableName || row.Depth != TableDepth)
        {
            return false;
        }

        // A folder can share the table's name while the status bar retains that table.
        // Require Properties itself to identify the anchor before trusting inferred depth.
        for (var attempt = 0; attempt < ViewReads; attempt++)
        {
            var view = await tree.View();
            if (view.Name == tableName && TomSelection.ObjectTypeMatches("Table", view.Type))
            {
                return true;
            }

            await tree.Wait(ViewPollMs);
        }

        return false;
    }

    private static async Task<(RowRead Read, TreeRow? Table, List<string> Searches)> FindTable(
        IRowTree tree,
        string name
    )
    {
        var searches = new List<string> { name };
        await tree.Search(name);
        var read = await tree.Read();
        var table = TableRow(read.Rows, name);
        var token = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (table == null && token is { } first && first != name)
        {
            searches.Add(first);
            await tree.Search(first);
            read = await tree.Read();
            table = TableRow(read.Rows, name);
        }

        return (read, table, searches);
    }

    private static TreeRow? TableRow(IReadOnlyList<TreeRow>? rows, string name) =>
        rows == null ? null : rows.FirstOrDefault(r => r.Value == name && SearchPick.TableOf(rows, r.Index) == name);

    // The row right below the anchor is deeper: the table is open.
    private static bool ChildBelow(IReadOnlyList<TreeRow>? rows, int index) =>
        rows?.FirstOrDefault(r => r.Index == index + 1)?.Depth > TableDepth;

    // The pick in the rows below the table, then, with no candidate, its display folders opened in turn: the folder
    // path's segments when one was asked for, else rows at any depth below the table in tree order. The latter lets
    // a folderless request descend through nested display folders; each row is typed before it can be opened.
    private static async Task<SelectResult> UnderTable(
        IRowTree tree,
        WalkRequest request,
        Func<bool> outOfTime,
        RowRead read,
        Anchor anchor
    )
    {
        var picked = await Pick(tree, read, request, outOfTime, anchor);
        var opened = new List<string>();
        var selected = new List<string>();
        var probed = new HashSet<int>();
        while (
            MayOpen(picked, opened.Count, selected.Count, outOfTime)
            && NextToOpen(read.Rows, request, anchor.Index, selected.Count, probed) is { } row
        )
        {
            selected.Add(row.Value ?? "");
            probed.Add(row.Index);
            if (!await OpenIfFolder(tree, row, request))
            {
                // An explicit folder path cannot continue below a segment TE3 did not identify as a folder.
                if (request.Folder.Length > 0)
                {
                    break;
                }

                continue;
            }

            opened.Add(row.Value ?? "");
            (read, _) = await tree.ReadBelow();
            picked = await Pick(tree, read, request, outOfTime, anchor);
        }

        return picked with
        {
            Tried = picked.Tried + 1 + selected.Count,
            ByTable = true,
            Pages = read.Pages,
            TruncatedByViewport = read.TruncatedByViewport,
            Reason = NoCandidate(picked) ? Unopened(request, read, opened, selected) : picked.Reason,
        };
    }

    private static bool NoCandidate(SelectResult picked) => !picked.Found && picked.Tried == 0;

    // Another row may be opened: nothing placed yet, both bounds unspent, and the clock still running.
    private static bool MayOpen(SelectResult picked, int opened, int selected, Func<bool> outOfTime) =>
        NoCandidate(picked) && opened < MaxFolderOpens && selected < MaxProbes && !outOfTime();

    // A folder path's segment is selected and independently typed by Properties before it is opened. The status bar
    // can retain a prior selection of the same name. Without a path, a row with no readable type may still be probed.
    private static async Task<bool> OpenIfFolder(IRowTree tree, TreeRow row, WalkRequest request)
    {
        if ((await tree.ReadSelected()).Selected != row.Index)
        {
            await tree.Select(row);
        }

        if (request.Folder.Length > 0)
        {
            for (var attempt = 0; attempt < ProbeReads; attempt++)
            {
                var view = await tree.View();
                if (view.Name == row.Value && view.Type?.Replace(" ", "", StringComparison.Ordinal) == "Folder")
                {
                    await tree.Expand();
                    return true;
                }

                await tree.Wait(ViewPollMs);
            }

            return false;
        }

        Confirmation? seen = null;
        for (var attempt = 0; attempt < ProbeReads && seen == null; attempt++)
        {
            seen = SelectionView.Read(row, await tree.View());
            if (seen == null)
            {
                await tree.Wait(ViewPollMs);
            }
        }

        if (seen?.Type is { } type && type.Replace(" ", "", StringComparison.Ordinal) != "Folder")
        {
            return false;
        }

        await tree.Expand();
        return true;
    }

    // The next row to open under the table: the folder path's next segment at its exact depth, or the first row
    // below the table not selected yet that is neither the object itself nor in the Partitions subtree. TreeItem indices
    // of rows already probed remain stable as this forward walk opens later rows; names need not be unique.
    private static TreeRow? NextToOpen(
        IReadOnlyList<TreeRow>? rows,
        WalkRequest request,
        int anchor,
        int selectedCount,
        HashSet<int> probed
    )
    {
        if (rows == null)
        {
            return null;
        }

        var under = RowsUnder(rows, anchor);
        var segments = request.Folder.Split(
            '\\',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        if (segments.Length > 0)
        {
            var next = selectedCount;
            return next < segments.Length
                ? under.FirstOrDefault(r => r.Depth == TableDepth + 1 + next && r.Value == segments[next])
                : null;
        }

        return ProbeRows(under).FirstOrDefault(r => r.Value != request.ObjectName && !probed.Contains(r.Index));
    }

    // The Partitions group and its children never contain display folders of columns or measures.
    private static IEnumerable<TreeRow> ProbeRows(IEnumerable<TreeRow> under)
    {
        var partitions = false;
        foreach (var row in under)
        {
            if (row.Depth == TableDepth + 1)
            {
                partitions = row.Value == PartitionsGroup;
            }

            if (!partitions)
            {
                yield return row;
            }
        }
    }

    // The rows below the table's row that belong to it: until the next row at its depth or above, or of unknown depth.
    private static IEnumerable<TreeRow> RowsUnder(IReadOnlyList<TreeRow> rows, int anchor) =>
        rows.Where(r => r.Index > anchor).OrderBy(r => r.Index).TakeWhile(r => r.Depth > TableDepth);

    private static string Unopened(WalkRequest request, RowRead read, List<string> opened, List<string> selected) =>
        $"No row named '{request.ObjectName}' under table '{request.Table}'"
        + (request.Folder.Length > 0 ? $" in folder '{request.Folder}'" : "")
        + $" among the {read.Rows?.Count ?? 0} rows on screen below the table's row once the search cleared, with "
        + $"{opened.Count} folder(s) under it opened ({string.Join(", ", opened)}); {selected.Count} row(s) were "
        + "selected under it and nothing else"
        + (read.TruncatedByViewport ? "; the table's rows run past the screen, which was not scrolled." : ".");
}
