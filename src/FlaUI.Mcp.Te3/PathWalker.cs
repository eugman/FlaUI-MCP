/// <summary>What the TOM Explorer shows for the selected row: its text and the status bar's object type.</summary>
/// <param name="Value">The selected tree row's text, e.g. "Date".</param>
/// <param name="Type">The status bar's type for the selection, e.g. "Calculated Table", or null.</param>
/// <param name="Depth">The row's depth in the tree (model root 0, Tables 1, a table 2), or null when unread.</param>
public sealed record RowObservation(string? Value, string? Type, int? Depth = null);

/// <summary>The keyboard view of TOM Explorer that <see cref="PathWalker"/> drives.</summary>
public interface ITreeCursor
{
    /// <summary>The selected row now.</summary>
    Task<RowObservation> Current();

    /// <summary>
    /// Press Down and return the next row once the selection and status bar have moved and settled; null when it did not
    /// move, at the end of the tree or because the tree was slow (see <see cref="Stalled"/>).
    /// </summary>
    Task<RowObservation?> Down();

    /// <summary>
    /// After a Down that returned null: true when a row was shown below the selection, so the selection should have
    /// moved and the end of the tree is not proven.
    /// </summary>
    bool Stalled => false;

    /// <summary>Press Left on the selected row to collapse it; false when it could not be shown to have collapsed.</summary>
    Task<bool> Collapse() => Task.FromResult(false);
}

/// <summary>Where a walk ended: the table the object was found under, or why it was not found.</summary>
/// <param name="Rescuable">
/// The walk stopped without deciding (the clock, the row limit, the end of the tree or a slow move) on a row of known
/// depth inside the requested folder: only then may a read of the selection still prove it reached the object. A
/// miss the walk decided (outside the folder, depth unknown, past the table) never is.
/// </param>
public sealed record WalkResult(bool Found, string? Table, int RowsVisited, string? Reason, bool Rescuable = false);

/// <summary>
/// Finds an object in TOM Explorer by walking the selection down a search-filtered tree. DevExpress only exposes on-screen
/// rows to UI Automation and offers no ItemContainer, Scroll or ExpandCollapse patterns, so a row is never looked up and
/// clicked: the walker only trusts the selected row (always scrolled into view) and the status bar. A table row sets the
/// table that the rows after it belong to, which is how a "Date" column under another table is told apart.
/// </summary>
public static class PathWalker
{
    public const int DefaultMaxRows = 400;

    /// <summary>Walk down from the current row until the object is selected.</summary>
    /// <param name="cursor">The tree, positioned on its first row.</param>
    /// <param name="table">The table that owns the object (the table itself when <paramref name="objectType"/> is Table).</param>
    /// <param name="objectName">The object's name as TOM Explorer shows it.</param>
    /// <param name="objectType">Table, Column or Measure.</param>
    /// <param name="maxRows">Rows to visit before giving up.</param>
    /// <param name="outOfTime">True once the walk must stop to finish inside the tool deadline.</param>
    /// <param name="folder">
    /// The object's display folder path, backslash-separated, or null: the object must then be inside it, and the walk
    /// stops once it leaves the table (see <see cref="WalkPlace"/>). Columns and measures only.
    /// </param>
    public static async Task<WalkResult> Find(
        ITreeCursor cursor,
        string table,
        string objectName,
        string objectType,
        int maxRows = DefaultMaxRows,
        Func<bool>? outOfTime = null,
        string? folder = null
    )
    {
        var place = new WalkPlace(table, folder);
        if (place.HasFolder && objectType == "Table")
        {
            throw new ArgumentException("folder applies to columns and measures; a table is in no display folder.");
        }

        var row = await cursor.Current();
        for (var visited = 1; visited <= maxRows; visited++)
        {
            if (Ends(place, row, visited, table, objectName, objectType) is { } ended)
            {
                return ended;
            }

            if (visited == maxRows)
            {
                break;
            }

            await SkipGroup(cursor, row);

            // Stop on our own before Core's fixed tool deadline cancels a key press part-way.
            if (outOfTime?.Invoke() == true)
            {
                return new(
                    false,
                    place.Table,
                    visited,
                    $"Stopped before the tool deadline after {visited} rows, on '{row.Value}' ({row.Type ?? "unknown type"}) "
                        + $"under table '{place.Table ?? "none"}'. Narrow the search or retry from here.",
                    Undecided(place, row)
                );
            }

            if (await cursor.Down() is not { } next)
            {
                return cursor.Stalled
                    ? new(false, place.Table, visited, DidNotMove(row, visited), Undecided(place, row))
                    : new(
                        false,
                        place.Table,
                        visited,
                        $"Reached the end of the filtered tree after {visited} rows.",
                        Undecided(place, row)
                    );
            }

            row = next;
        }

        return new(false, place.Table, maxRows, $"Stopped after {maxRows} rows.", Undecided(place, row));
    }

    // A stop the walk did not decide, on a placed row inside the folder: a read of the selection may still settle it.
    private static bool Undecided(WalkPlace place, RowObservation row) => row.Depth != null && place.InFolder;

    // Roles, Perspectives and the other groups hold no table, column or measure the walk could want.
    private static async Task SkipGroup(ITreeCursor cursor, RowObservation row)
    {
        if (WalkPlace.SkipsGroup(row))
        {
            await cursor.Collapse();
        }
    }

    // The walk ends on this row: it is the object, it is the object outside the folder, or it leaves the table.
    private static WalkResult? Ends(
        WalkPlace place,
        RowObservation row,
        int visited,
        string table,
        string objectName,
        string objectType
    )
    {
        if (place.Enter(row) is { } left)
        {
            return new(false, table, visited, left);
        }

        if (!Matches(row, place.Table, table, objectName, objectType))
        {
            return null;
        }

        return place.InFolder
            ? new(true, place.Table, visited, null)
            : new(false, place.Table, visited, place.OutsideFolder(objectName));
    }

    /// <summary>
    /// Presses Down once and returns the row below, or null at the end of the tree. A Down that did not move while a
    /// row was shown below throws: the caller would otherwise take the current row for the last one.
    /// </summary>
    /// <param name="cursor">The tree, on <paramref name="current"/>.</param>
    /// <param name="current">The selected row, already read.</param>
    public static async Task<RowObservation?> RowAfter(ITreeCursor cursor, RowObservation current)
    {
        var next = await cursor.Down();
        if (next == null && cursor.Stalled)
        {
            throw new InvalidOperationException(DidNotMove(current, 1));
        }

        return next;
    }

    /// <summary>Walk down to the first row showing the value, in tree order.</summary>
    /// <param name="cursor">The tree, on the row to start from.</param>
    /// <param name="value">The row text to select.</param>
    /// <param name="boundary">The row after the subtree being searched; reaching it means the value is not in it.</param>
    /// <param name="skipCurrent">Start with the row below the current one.</param>
    /// <param name="maxRows">Rows to visit before giving up.</param>
    public static async Task<WalkResult> FindRow(
        ITreeCursor cursor,
        string value,
        RowObservation? boundary = null,
        bool skipCurrent = false,
        int maxRows = DefaultMaxRows
    )
    {
        var row = skipCurrent ? await cursor.Down() : await cursor.Current();
        RowObservation? previous = null;
        for (var visited = 1; visited <= maxRows; visited++)
        {
            if (row == null)
            {
                return new(
                    false,
                    null,
                    visited - 1,
                    cursor.Stalled
                        ? DidNotMove(previous, visited - 1)
                        : $"Reached the end of the tree after {visited - 1} rows."
                );
            }

            // The whole row, text and status-bar type: a child may share the next table's name.
            if (boundary != null && row == boundary)
            {
                return new(
                    false,
                    null,
                    visited,
                    $"Reached '{boundary.Value}', the row after the expanded parent, so '{value}' is not under it."
                );
            }

            if (row.Value == value)
            {
                return new(true, null, visited, null);
            }

            previous = row;
            row = visited < maxRows ? await cursor.Down() : null;
        }

        return new(false, null, maxRows, $"Stopped after {maxRows} rows.");
    }

    // A Down that timed out while a row was shown below: a slow read, not the end of the tree.
    private static string DidNotMove(RowObservation? row, int visited) =>
        $"The selection did not move from '{row?.Value ?? "the starting row"}' within "
        + $"{WalkClock.MoveTimeout.TotalSeconds} s after {visited} rows, although a row is shown below it. TOM Explorer "
        + "was slow; this is not proof the object is missing. Observe with te3_model_state, then retry.";

    internal static bool IsTable(string? type) => Te3Page.ObjectTypeMatches("Table", type);

    /// <summary>
    /// The selection is the requested object: its row (or Properties, when the row's type was not read) has the name
    /// and type, the walk reached it through the requested table, and Properties, when shown, names the same object.
    /// </summary>
    /// <param name="propertiesName">Properties' Name for the selection, or null when it was not read.</param>
    /// <param name="propertiesType">Properties' ObjectType for the selection, or null when it was not read.</param>
    internal static bool Arrived(
        RowObservation row,
        string? propertiesName,
        string? propertiesType,
        string? walkedTable,
        string table,
        string objectName,
        string objectType
    )
    {
        var typed = row with { Type = row.Type ?? propertiesType };
        return Matches(typed, walkedTable, table, objectName, objectType)
            && (propertiesName == null || propertiesName == objectName);
    }

    internal static bool Matches(
        RowObservation row,
        string? currentTable,
        string table,
        string objectName,
        string objectType
    ) => row.Value == objectName && Te3Page.ObjectTypeMatches(objectType, row.Type) && currentTable == table;
}
