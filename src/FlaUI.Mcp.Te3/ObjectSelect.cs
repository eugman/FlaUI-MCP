/// <summary>One read of the filtered tree: its rows (null when no depth is known), and what may be missing from it.</summary>
/// <param name="Complete">False when the tree may hold rows that are not realized (it scrolls, or the read was cut).</param>
/// <param name="Truncated">The read hit its element limit: rows past the cut are not in it.</param>
public sealed record RowRead(IReadOnlyList<TreeRow>? Rows, bool Complete, double? IndentPx, bool Truncated);

/// <summary>The search-filtered TOM Explorer as the row strategy uses it: one read of every realized row, one select.</summary>
public interface IRowTree
{
    /// <summary>Every realized row with its depth, in tree order; null rows when the indent could not be measured.</summary>
    Task<RowRead> Read();

    /// <summary>Selects the row with one guarded SelectionItem.Select (Core falls back to a click when it is ignored).</summary>
    Task Select(TreeRow row);

    /// <summary>A fresh read after a select: the rows, and the index of the TreeItem that is selected now.</summary>
    Task<(IReadOnlyList<TreeRow>? Rows, int? Selected)> ReadSelected();

    /// <summary>Properties' Name and ObjectType for the selection, or nulls when they cannot be read.</summary>
    Task<(string? Name, string? Type)> Selected();
}

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
    IReadOnlyList<RejectedRow>? RejectedRows = null
)
{
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
        if (candidates.Count == 0)
        {
            return new(
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

        var rejected = new List<RejectedRow>();
        TreeRow? last = null;
        foreach (var row in candidates.Take(MaxTries).TakeWhile(_ => !outOfTime()))
        {
            await tree.Select(row);
            last = row;
            if (await Confirmed(tree, row, request) is { } why)
            {
                rejected.Add(new(row.Index, row.Value, why));
                continue;
            }

            return new(true, row, rows.Count, read.IndentPx, rejected.Count + 1, null, false, read.Truncated, rejected);
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

    // Why the selected candidate is not the object, or null when it is: a fresh read must show this very row selected,
    // still in the requested table and folder, before Properties' Name and ObjectType are trusted.
    private static async Task<string?> Confirmed(IRowTree tree, TreeRow row, WalkRequest request)
    {
        var (fresh, selected) = await tree.ReadSelected();
        if (selected != row.Index)
        {
            return $"the selection is row {selected?.ToString() ?? "unknown"}, which is not row {row.Index}";
        }

        if (fresh == null || SearchPick.Candidates(fresh, request).Candidates.All(c => c.Index != row.Index))
        {
            return "a fresh read does not place it in the requested table and folder";
        }

        var (name, type) = await tree.Selected();
        return name == request.ObjectName && Te3Page.ObjectTypeMatches(request.ObjectType, type)
            ? null
            : $"Properties shows {type ?? "no type"} '{name ?? "no name"}'";
    }

    private static string Missing(WalkRequest request, IReadOnlyList<TreeRow> rows) =>
        $"No row named '{request.ObjectName}' under table '{request.Table}'"
        + (request.Folder.Length > 0 ? $" in folder '{request.Folder}'" : "")
        + $" among the {rows.Count} rows the search showed; nothing was selected.";
}
