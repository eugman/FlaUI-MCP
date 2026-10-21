/// <summary>One read of the filtered tree: its rows (null when no depth is known), and what may be missing from it.</summary>
/// <param name="Complete">False when the tree may hold rows that are not realized (it scrolls, or the read was cut).</param>
/// <param name="Truncated">The read hit its element limit: rows past the cut are not in it.</param>
/// <param name="Pages">PageDown presses used to realize rows past the viewport.</param>
/// <param name="TruncatedByViewport">Paging stopped while the viewport still hid rows.</param>
public sealed record RowRead(
    IReadOnlyList<TreeRow>? Rows,
    bool Complete,
    double? IndentPx,
    bool Truncated,
    int Pages = 0,
    bool TruncatedByViewport = false
);

/// <summary>The search-filtered TOM Explorer as the row strategy uses it: one read of every realized row, one select.</summary>
public interface IRowTree
{
    /// <summary>Every realized row with its depth, in tree order; null rows when the indent could not be measured.</summary>
    Task<RowRead> Read();

    /// <summary>Selects the row with one guarded SelectionItem.Select (Core falls back to a click when it is ignored).</summary>
    Task Select(TreeRow row);

    /// <summary>A fresh read after a select: the rows, and the index of the TreeItem that is selected now.</summary>
    Task<(IReadOnlyList<TreeRow>? Rows, int? Selected)> ReadSelected();

    /// <summary>
    /// What the selection is: its name and type from the status bar's selection slot when that names
    /// <paramref name="row"/>, otherwise Properties' Name and ObjectType; and Properties' display Folder.
    /// </summary>
    Task<Confirmation> Selected(TreeRow row);
}

/// <summary>What TE3 says the selection is; any part may be null when it cannot be read.</summary>
public sealed record Confirmation(string? Name, string? Type, string? Folder);

/// <summary>A candidate the row strategy selected and turned down, and why.</summary>
public sealed record RejectedRow(int Index, string? Value, string Reason);

/// <summary>What the row strategy did: the row it selected, how many rows it read, and why it fell back or missed.</summary>
public sealed record SelectResult(
    bool Found,
    TreeRow? Row,
    int RowsRead,
    double? IndentPx,
    int Tried,
    string? Reason,
    bool FallBack,
    bool Truncated = false,
    IReadOnlyList<RejectedRow>? RejectedRows = null,
    string? ConfirmedType = null
)
{
    /// <summary>PageDown presses the read used, and whether the viewport still hid rows when it stopped.</summary>
    public int Pages { get; init; }

    public bool TruncatedByViewport { get; init; }

    public IReadOnlyList<RejectedRow> Rejected => RejectedRows ?? [];
}

/// <summary>
/// te3_navigate object's primary strategy: read the filtered rows once, pick the object by names and depth
/// (<see cref="SearchPick"/>), select it, and confirm by Properties' Name and ObjectType. No row is walked with keys.
/// Falls back to the keyboard walk only when depth is unknown or the tree may hold unrealized rows.
/// </summary>
internal static class ObjectSelect
{
    /// <summary>At most this many same-named candidates are selected in turn (a column and a hierarchy level).</summary>
    public const int MaxTries = 3;

    public static async Task<SelectResult> Run(IRowTree tree, WalkRequest request, Func<bool> outOfTime)
    {
        var read = await tree.Read();
        var result = await Pick(tree, read, request, outOfTime);
        return result with { Pages = read.Pages, TruncatedByViewport = read.TruncatedByViewport };
    }

    private static async Task<SelectResult> Pick(IRowTree tree, RowRead read, WalkRequest request, Func<bool> outOfTime)
    {
        if (Unpicked(read, request) is { } early)
        {
            return early;
        }

        var rows = read.Rows!;
        var candidates = SearchPick.Candidates(rows, request).Candidates;
        var rejected = new List<RejectedRow>();
        TreeRow? last = null;
        foreach (var row in candidates.Take(MaxTries).TakeWhile(_ => !outOfTime()))
        {
            await tree.Select(row);
            last = row;
            var (why, type) = await Confirmed(tree, row, request);
            if (why != null)
            {
                rejected.Add(new(row.Index, row.Value, why));
                continue;
            }

            return new(
                true,
                row,
                rows.Count,
                read.IndentPx,
                rejected.Count + 1,
                null,
                false,
                read.Truncated,
                rejected,
                type
            );
        }

        return new(
            false,
            last,
            rows.Count,
            read.IndentPx,
            rejected.Count,
            $"Selected {rejected.Count} row(s) named '{request.ObjectName}' in place and turned each down; the "
                + $"selection ended on row {last?.Index} '{last?.Value}'.",
            false,
            read.Truncated,
            rejected
        );
    }

    // No row to select: the depth is unknown, or nothing in the read is in place (fall back when rows may be missing).
    private static SelectResult? Unpicked(RowRead read, WalkRequest request)
    {
        if (read.Rows is not { } rows)
        {
            return new(
                false,
                null,
                0,
                read.IndentPx,
                0,
                "The tree's indent could not be measured, so no row's depth is known.",
                true,
                read.Truncated
            );
        }

        var (candidates, unplaced) = SearchPick.Candidates(rows, request);
        return candidates.Count > 0
            ? null
            : new(
                false,
                null,
                rows.Count,
                read.IndentPx,
                0,
                Missing(request, rows),
                !read.Complete || read.Truncated || unplaced,
                read.Truncated
            );
    }

    // Why the selected candidate is not the object (null when it is), and its type: a fresh read must show this very
    // row selected, still in the requested table and folder, before TE3's own account of the selection is compared.
    // A column's Properties grid has no ObjectType row (3.27.1), so the type comes from the status bar first.
    private static async Task<(string? Why, string? Type)> Confirmed(IRowTree tree, TreeRow row, WalkRequest request)
    {
        var (fresh, selected) = await tree.ReadSelected();
        if (selected != row.Index)
        {
            return ($"the selection is row {selected?.ToString() ?? "unknown"}, which is not row {row.Index}", null);
        }

        if (fresh == null || SearchPick.Candidates(fresh, request).Candidates.All(c => c.Index != row.Index))
        {
            return ("a fresh read does not place it in the requested table and folder", null);
        }

        var seen = await tree.Selected(row);
        return (Mismatch(seen, request), seen.Type);
    }

    private static string? Mismatch(Confirmation seen, WalkRequest request)
    {
        if (seen.Name != request.ObjectName)
        {
            return $"TE3 names the selection '{seen.Name ?? "nothing"}'";
        }

        if (request.ObjectType.Length > 0 && !Te3Page.ObjectTypeMatches(request.ObjectType, seen.Type))
        {
            return $"TE3 shows it as {seen.Type ?? "no type"}, not {request.ObjectType}";
        }

        return request.Folder.Length > 0 && seen.Folder is { } folder && folder != request.Folder
            ? $"Properties shows it in folder '{folder}'"
            : null;
    }

    private static string Missing(WalkRequest request, IReadOnlyList<TreeRow> rows) =>
        $"No row named '{request.ObjectName}' under table '{request.Table}'"
        + (request.Folder.Length > 0 ? $" in folder '{request.Folder}'" : "")
        + $" among the {rows.Count} rows the search showed; nothing was selected.";
}
