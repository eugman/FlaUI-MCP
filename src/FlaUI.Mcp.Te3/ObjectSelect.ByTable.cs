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
        // With a folder path its folders are opened segment by segment; without one each row directly under the
        // table in turn (a column takes the Right and shows nothing more).
        while (
            NoCandidate(picked)
            && opened.Count < MaxFolderOpens
            && !outOfTime()
            && NextToOpen(read.Rows, request, opened) is { } folder
        )
        {
            opened.Add(folder.Value ?? "");
            await Open(tree, folder);
            read = await tree.Read();
            picked = await Pick(tree, read, request, outOfTime);
        }

        return picked with
        {
            Tried = picked.Tried + 1 + opened.Count,
            ByTable = true,
            Pages = read.Pages,
            TruncatedByViewport = read.TruncatedByViewport,
            Reason = NoCandidate(picked) && opened.Count > 0 ? Unopened(request, read, opened) : picked.Reason,
        };
    }

    // Selected, unless it is, then Right.
    private static async Task Open(IRowTree tree, TreeRow row)
    {
        if ((await tree.ReadSelected()).Selected != row.Index)
        {
            await tree.Select(row);
        }

        await tree.Expand();
    }

    private static bool NoCandidate(SelectResult picked) => !picked.Found && picked.Tried == 0;

    // The next row to open under the table: the folder path's next segment at its exact depth, or the first row
    // directly under the table not opened yet that is not the object itself.
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
            r.Depth == TableDepth + 1 && r.Value != request.ObjectName && !opened.Contains(r.Value ?? "")
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

    private static string Unopened(WalkRequest request, RowRead read, List<string> opened) =>
        $"No row named '{request.ObjectName}' under table '{request.Table}'"
        + (request.Folder.Length > 0 ? $" in folder '{request.Folder}'" : "")
        + $" among the {read.Rows?.Count ?? 0} rows the table's name showed with {opened.Count} folder(s) under it "
        + $"opened ({string.Join(", ", opened)}); only those were selected.";
}
