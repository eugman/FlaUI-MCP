/// <summary>What the TOM Explorer shows for the selected row: its text and the status bar's object type.</summary>
/// <param name="Value">The selected tree row's text, e.g. "Date".</param>
/// <param name="Type">The status bar's type for the selection, e.g. "Calculated Table", or null.</param>
public sealed record RowObservation(string? Value, string? Type);

/// <summary>The keyboard view of TOM Explorer that <see cref="PathWalker"/> drives.</summary>
public interface ITreeCursor
{
    /// <summary>The selected row now.</summary>
    Task<RowObservation> Current();

    /// <summary>Press Down and return the next row once the selection and status bar have moved and settled; null at the end.</summary>
    Task<RowObservation?> Down();
}

/// <summary>Where a walk ended: the table the object was found under, or why it was not found.</summary>
public sealed record WalkResult(bool Found, string? Table, int RowsVisited, string? Reason);

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
    public static async Task<WalkResult> Find(
        ITreeCursor cursor,
        string table,
        string objectName,
        string objectType,
        int maxRows = DefaultMaxRows
    )
    {
        string? currentTable = null;
        var row = await cursor.Current();
        for (var visited = 1; visited <= maxRows; visited++)
        {
            if (IsTable(row.Type))
            {
                currentTable = row.Value;
            }

            if (Matches(row, currentTable, table, objectName, objectType))
            {
                return new(true, currentTable, visited, null);
            }

            if (visited == maxRows)
            {
                break;
            }

            if (await cursor.Down() is not { } next)
            {
                return new(false, null, visited, $"Reached the end of the filtered tree after {visited} rows.");
            }

            row = next;
        }

        return new(false, null, maxRows, $"Stopped after {maxRows} rows.");
    }

    internal static bool IsTable(string? type) => Te3Page.ObjectTypeMatches("Table", type);

    internal static bool Matches(
        RowObservation row,
        string? currentTable,
        string table,
        string objectName,
        string objectType
    ) =>
        row.Value == objectName
        && Te3Page.ObjectTypeMatches(objectType, row.Type)
        && (objectType == "Table" ? row.Value == table : currentTable == table);
}
