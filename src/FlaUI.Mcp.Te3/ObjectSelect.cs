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

    /// <summary>The status bar's selection slot and Properties' Name, ObjectType and Folder, as they read now.</summary>
    Task<SelectionView> View();

    /// <summary>A pause between reads of the selection.</summary>
    Task Wait(int ms);
}

/// <summary>What TE3 says the selection is; any part may be null when it cannot be read.</summary>
public sealed record Confirmation(string? Name, string? Type, string? Folder);

/// <summary>The raw readings of the selection: the status bar's slot and the Properties rows.</summary>
public sealed record SelectionView(string? Bar, string? Name, string? Type, string? Folder)
{
    /// <summary>
    /// What TE3 says the selection is, when it names <paramref name="row"/>'s label: the status bar's slot first
    /// (its type), else Properties' Name (with Properties' ObjectType, which a column does not have). Null when
    /// neither names the label yet.
    /// </summary>
    public static Confirmation? Read(TreeRow row, SelectionView now)
    {
        if (TomSelection.SelectionName(now.Bar) == row.Value)
        {
            return new(row.Value, TomSelection.SelectionType(now.Bar), now.Folder);
        }

        return now.Name == row.Value ? new(now.Name, now.Type, now.Folder) : null;
    }
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
            // A row already selected (an earlier walk) is confirmed as it is: selecting it again sends input and,
            // in a tree, can start an inline rename.
            var (_, current) = await tree.ReadSelected();
            var sent = current != row.Index;
            if (sent)
            {
                await tree.Select(row);
            }

            last = row;
            var (why, type) = await Confirmed(tree, rows, row, request);
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

        if (rows.Count == 0)
        {
            // An empty read is never a reason to walk with keys: the filter showed nothing to walk.
            return new(false, null, 0, read.IndentPx, 0, "The search showed no rows; nothing was selected.", false);
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
    private static async Task<(string? Why, string? Type)> Confirmed(
        IRowTree tree,
        IReadOnlyList<TreeRow> paged,
        TreeRow row,
        WalkRequest request
    )
    {
        var (fresh, selected) = await SelectedNow(tree, row);
        if (selected != row.Index)
        {
            return ($"the selection is row {selected?.ToString() ?? "unknown"}, which is not row {row.Index}", null);
        }

        // The fresh read realizes only the screen around the row; its table and folders may be pages above.
        if (
            fresh == null
            || SearchPick.Candidates(Merged(paged, fresh), request).Candidates.All(c => c.Index != row.Index)
        )
        {
            return ("a fresh read does not place it in the requested table and folder", null);
        }

        var seen = await Settled(tree, row, request);
        return seen == null
            ? ($"neither the status bar nor Properties named '{row.Value}' within {ViewReads * ViewPollMs} ms", null)
            : (Mismatch(seen, request), seen.Type);
    }

    private const int SettleMs = 150;
    private const int SelectReads = 4;
    private const int ViewPollMs = 300;
    private const int ViewReads = 5;

    // The status bar names an object selection quickly; Properties follows a moment later and has no ObjectType row
    // for a column. A few short reads, never a key.
    // The fresh read that must show the picked row selected; a few short reads while the select settles.
    private static async Task<(IReadOnlyList<TreeRow>? Rows, int? Selected)> SelectedNow(IRowTree tree, TreeRow row)
    {
        var read = await tree.ReadSelected();
        for (var attempt = 1; attempt < SelectReads && read.Selected != row.Index; attempt++)
        {
            await tree.Wait(SettleMs);
            read = await tree.ReadSelected();
        }

        return read;
    }

    // Polls until TE3 names the picked label with the requested type (a slot left by an earlier same-named candidate
    // is waited out); after about 1.5 s the last reading naming the label is taken, so a real mismatch is reported.
    private static async Task<Confirmation?> Settled(IRowTree tree, TreeRow row, WalkRequest request)
    {
        Confirmation? last = null;
        for (var attempt = 0; attempt < ViewReads; attempt++)
        {
            var seen = SelectionView.Read(row, await tree.View());
            if (
                seen != null
                && (request.ObjectType.Length == 0 || TomSelection.ObjectTypeMatches(request.ObjectType, seen.Type))
            )
            {
                return seen;
            }

            last = seen ?? last;
            await tree.Wait(ViewPollMs);
        }

        return last;
    }

    // The paged rows with the fresh read laid over them by index: placement is computed over the whole list.
    internal static IReadOnlyList<TreeRow> Merged(IReadOnlyList<TreeRow> paged, IReadOnlyList<TreeRow> fresh)
    {
        var rows = paged.ToDictionary(r => r.Index);
        foreach (var row in fresh)
        {
            rows[row.Index] = row;
        }

        return [.. rows.Values.OrderBy(r => r.Index)];
    }

    private static string? Mismatch(Confirmation seen, WalkRequest request)
    {
        if (seen.Name != request.ObjectName)
        {
            return $"TE3 names the selection '{seen.Name ?? "nothing"}'";
        }

        if (request.ObjectType.Length > 0 && !TomSelection.ObjectTypeMatches(request.ObjectType, seen.Type))
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
