/// <summary>
/// The row strategy's second try: when the object's own name shows no placed candidate (live, "Rate" and "Measure"
/// match calculation items in many tables and the filtered view gave no measurable indent), the tree is filtered by
/// the table's name instead, the table row is selected and opened, and the object is looked for under it. The
/// table-name filter shows the table's display folders collapsed (te3-eb2ed6a), so they are opened in turn, each
/// with one select and one Right, until the object is placed or <see cref="MaxFolderOpens"/> is reached.
/// </summary>
internal static partial class ObjectSelect
{
    /// <summary>Rows opened under the table at most, after the table itself; the 18 s clock bounds them too.</summary>
    public const int MaxFolderOpens = 4;

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
        if (!first.FallBack)
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

    private const int TableDepth = 2;

    public static async Task<SelectResult> ByTable(IRowTree tree, WalkRequest request, Func<bool> outOfTime)
    {
        await tree.Search(request.Table);
        var read = await tree.Read();
        var rows = read.Rows;
        var table = rows?.FirstOrDefault(r =>
            r.Value == request.Table && SearchPick.TableOf(rows, r.Index) == request.Table
        );
        if (table == null)
        {
            return new(
                false,
                null,
                rows?.Count ?? 0,
                read.IndentPx,
                0,
                $"Searching for the table's own name '{request.Table}' showed no row placed as that table.",
                true,
                read.Truncated
            );
        }

        await Open(tree, table);
        read = await tree.Read();
        var picked = await Pick(tree, read, request, outOfTime);
        var opened = new List<string>();
        var selected = new List<string>();
        // With a folder path its folders are opened segment by segment; without one each row directly under the
        // table in turn, selected first and opened only when TE3 does not type it as an object (a column or measure
        // takes the select and nothing more); Partitions never holds either and is skipped by name.
        while (
            MayOpen(picked, opened.Count, selected.Count, outOfTime)
            && NextToOpen(read.Rows, request, selected) is { } row
        )
        {
            selected.Add(row.Value ?? "");
            if (!await OpenIfFolder(tree, row, request))
            {
                continue;
            }

            opened.Add(row.Value ?? "");
            read = await tree.Read();
            picked = await Pick(tree, read, request, outOfTime);
        }

        return picked with
        {
            Tried = picked.Tried + 1 + selected.Count,
            ByTable = true,
            Pages = read.Pages,
            TruncatedByViewport = read.TruncatedByViewport,
            Reason =
                NoCandidate(picked) && selected.Count > 0 ? Unopened(request, read, opened, selected) : picked.Reason,
        };
    }

    /// <summary>Rows selected under the table at most, to tell its folders from its objects (each costs a select).</summary>
    public const int MaxProbes = 8;

    private const string PartitionsGroup = "Partitions";

    // Selected, unless it is, then Right.
    private static async Task Open(IRowTree tree, TreeRow row)
    {
        if ((await tree.ReadSelected()).Selected != row.Index)
        {
            await tree.Select(row);
        }

        await tree.Expand();
    }

    // A folder path's segment is opened as it is; any other row is selected first and opened only when TE3 types it
    // as a folder or not at all (the status bar does not follow a display folder; Properties says Folder).
    private static async Task<bool> OpenIfFolder(IRowTree tree, TreeRow row, WalkRequest request)
    {
        if (request.Folder.Length > 0)
        {
            await Open(tree, row);
            return true;
        }

        if ((await tree.ReadSelected()).Selected != row.Index)
        {
            await tree.Select(row);
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

    private const int ProbeReads = 3;

    private static bool NoCandidate(SelectResult picked) => !picked.Found && picked.Tried == 0;

    // Another row may be opened: nothing placed yet, both bounds unspent, and the clock still running.
    private static bool MayOpen(SelectResult picked, int opened, int selected, Func<bool> outOfTime) =>
        NoCandidate(picked) && opened < MaxFolderOpens && selected < MaxProbes && !outOfTime();

    // The next row to open under the table: the folder path's next segment at its exact depth, or the first row
    // directly under the table not selected yet that is neither the object itself nor the Partitions group.
    private static TreeRow? NextToOpen(IReadOnlyList<TreeRow>? rows, WalkRequest request, List<string> opened)
    {
        if (rows == null)
        {
            return null;
        }

        var under = RowsUnder(rows, request.Table);
        var segments = request.Folder.Split(
            '\\',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        if (segments.Length > 0)
        {
            var next = opened.Count;
            return next < segments.Length
                ? under.FirstOrDefault(r => r.Depth == TableDepth + 1 + next && r.Value == segments[next])
                : null;
        }

        return under.FirstOrDefault(r =>
            r.Depth == TableDepth + 1
            && r.Value != request.ObjectName
            && r.Value != PartitionsGroup
            && !opened.Contains(r.Value ?? "")
        );
    }

    // The rows under the requested table (under Tables): after its row, until the next row at its depth or above.
    private static IEnumerable<TreeRow> RowsUnder(IReadOnlyList<TreeRow> rows, string table)
    {
        var inside = false;
        foreach (var row in rows)
        {
            if (row.Depth is null or <= TableDepth)
            {
                inside = row.Depth == TableDepth && row.Value == table && SearchPick.TableOf(rows, row.Index) == table;
            }
            else if (inside)
            {
                yield return row;
            }
        }
    }

    private static string Unopened(WalkRequest request, RowRead read, List<string> opened, List<string> selected) =>
        $"No row named '{request.ObjectName}' under table '{request.Table}'"
        + (request.Folder.Length > 0 ? $" in folder '{request.Folder}'" : "")
        + $" among the {read.Rows?.Count ?? 0} rows the table's name showed with {opened.Count} folder(s) under it "
        + $"opened ({string.Join(", ", opened)}); {selected.Count} row(s) were selected under it and nothing else.";
}
