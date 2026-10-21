/// <summary>The search-filtered TOM Explorer as the row strategy uses it: one read of every realized row, one select.</summary>
public interface IRowTree
{
    /// <summary>
    /// Every realized row with its depth, in tree order, or null when the indent could not be measured (then no depth
    /// is known). <paramref name="complete"/> is false when the tree may hold rows that are not realized (it scrolls).
    /// </summary>
    Task<(IReadOnlyList<TreeRow>? Rows, bool Complete, double? IndentPx)> Read();

    /// <summary>Selects the row with one guarded SelectionItem.Select (Core falls back to a click when it is ignored).</summary>
    Task Select(TreeRow row);

    /// <summary>Properties' Name and ObjectType for the selection, or nulls when they cannot be read.</summary>
    Task<(string? Name, string? Type)> Selected();
}

/// <summary>What the row strategy did: the row it selected, how many rows it read, and why it fell back or missed.</summary>
public sealed record SelectResult(
    bool Found,
    TreeRow? Row,
    int RowsRead,
    double? IndentPx,
    int Tried,
    string? Reason,
    bool FallBack
);

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
        var (rows, complete, indent) = await tree.Read();
        if (rows == null)
        {
            return new(
                false,
                null,
                0,
                indent,
                0,
                "The tree's indent could not be measured, so no row's depth is known.",
                true
            );
        }

        var candidates = SearchPick.Candidates(rows, request);
        if (candidates.Count == 0)
        {
            return new(false, null, rows.Count, indent, 0, Missing(request, rows), !complete);
        }

        TreeRow? last = null;
        var tried = 0;
        foreach (var row in candidates.Take(MaxTries).TakeWhile(_ => !outOfTime()))
        {
            await tree.Select(row);
            tried++;
            last = row;
            var (name, type) = await tree.Selected();
            if (name == request.ObjectName && Te3Page.ObjectTypeMatches(request.ObjectType, type))
            {
                return new(true, row, rows.Count, indent, tried, null, false);
            }
        }

        return new(
            false,
            last,
            rows.Count,
            indent,
            tried,
            $"Selected {tried} row(s) named '{request.ObjectName}' in place; Properties showed none of them as a "
                + $"{request.ObjectType}. The selection ended on row {last?.Index} '{last?.Value}'.",
            false
        );
    }

    private static string Missing(WalkRequest request, IReadOnlyList<TreeRow> rows) =>
        $"No row named '{request.ObjectName}' under table '{request.Table}'"
        + (request.Folder.Length > 0 ? $" in folder '{request.Folder}'" : "")
        + $" among the {rows.Count} rows the search showed; nothing was selected.";
}
