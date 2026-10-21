/// <summary>
/// Where an object walk is in the filtered tree: the table the rows belong to, and how much of the requested display
/// folder path it has passed under the requested table. The search filter shows every ancestor of a match, so the
/// walk passes the table row, then each folder row of the path in turn, then the rows inside the folder. Rows carry no
/// depth, so a folder is matched by its rows' order: each segment must follow the one before it under the table.
/// </summary>
internal sealed class WalkPlace(string table, string? folder)
{
    // Rows of these types are objects, not display folders, even when one shares a folder's name.
    private static readonly string[] NotFolders = ["Hierarchy", "Level", "Partition", "Relationship"];

    private readonly string[] segments = (folder ?? "").Split(
        '\\',
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
    );

    private int passed;

    /// <summary>The table the current row belongs to, or null before the first table row.</summary>
    public string? Table { get; private set; }

    /// <summary>True when no folder was asked for, or the walk has passed every folder of the path.</summary>
    public bool InFolder => passed == segments.Length;

    public bool HasFolder => segments.Length > 0;

    /// <summary>
    /// Moves the walk onto <paramref name="row"/>. Returns why the object cannot be further on when a folder was asked
    /// for and this row leaves the requested table; otherwise null.
    /// </summary>
    public string? Enter(RowObservation row)
    {
        if (PathWalker.IsTable(row.Type))
        {
            var left = HasFolder && Table == table ? LeftTable() : null;
            Table = row.Value;
            passed = 0;
            return left;
        }

        if (Table == table && !InFolder && IsFolder(row) && row.Value == segments[passed])
        {
            passed++;
        }

        return null;
    }

    /// <summary>The object was found under the table, but before the walk passed the folder path.</summary>
    public string OutsideFolder(string objectName) =>
        $"Found '{objectName}' under table '{table}', but not inside display folder '{folder}': it is in another "
        + "folder or none. Object names are unique in a table, so the walk stopped there.";

    private string LeftTable() =>
        passed == 0
            ? $"Walked through table '{table}' and found no display folder '{folder}' under it for this search."
            : $"Walked through display folder '{folder}' under table '{table}' without finding the object.";

    private static bool IsFolder(RowObservation row) =>
        !Te3Page.ObjectTypeMatches("Column", row.Type)
        && !Te3Page.ObjectTypeMatches("Measure", row.Type)
        && !NotFolders.Contains(row.Type?.Replace(" ", "", StringComparison.Ordinal));
}
