/// <summary>
/// Where an object walk is in the filtered tree: the table the rows belong to, and which folders of the requested
/// display folder path it is inside under the requested table. The search filter shows every ancestor of a match, so
/// the walk passes the table row, then each folder row of the path, then the rows inside the folder.
/// </summary>
/// <remarks>
/// Depth comes from each row's left edge, which TOM Explorer indents under its parent: a row at or left of a matched
/// folder's edge has left that folder. Whether rows are indented at all is decided by the first row under the table;
/// when it is not to the right of the table row (or an edge is unknown), placement is checked by row order only, and
/// a miss says so.
/// </remarks>
internal sealed class WalkPlace(string table, string? folder)
{
    // Rows of these types are objects, not display folders, even when one shares a folder's name.
    private static readonly string[] NotFolders = ["Hierarchy", "Level", "Partition", "Relationship"];

    private readonly string[] segments = (folder ?? "").Split(
        '\\',
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
    );

    // The folders the walk is inside under the table, innermost on top: each one's left edge, and whether it is the
    // next folder of the path (an unmatched folder between path folders means the path does not continue there).
    private readonly Stack<(double Left, bool OnPath)> inside = new();
    private int matched;
    private double? tableLeft;
    private bool? indented;
    private bool seenFolder;

    /// <summary>The table the current row belongs to, or null before the first table row.</summary>
    public string? Table { get; private set; }

    /// <summary>True when no folder was asked for, or the walk is inside every folder of the path.</summary>
    public bool InFolder => matched == segments.Length;

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
            tableLeft = row.Left;
            indented = null;
            inside.Clear();
            matched = 0;
            return left;
        }

        if (Table == table && HasFolder)
        {
            Track(row);
        }

        return null;
    }

    private void Track(RowObservation row)
    {
        indented ??= tableLeft is { } t && row.Left is { } l ? l > t : false;
        while (indented == true && inside.Count > 0 && row.Left <= inside.Peek().Left)
        {
            matched -= inside.Pop().OnPath ? 1 : 0;
        }

        if (!IsFolder(row))
        {
            return;
        }

        var continues = !InFolder && (inside.Count == 0 || inside.Peek().OnPath);
        if (continues && row.Value == segments[matched])
        {
            inside.Push((row.Left ?? 0, true));
            matched++;
            seenFolder = true;
        }
        else if (indented == true)
        {
            inside.Push((row.Left ?? 0, false));
        }
    }

    /// <summary>The object was found under the table, but not inside the folder path.</summary>
    public string OutsideFolder(string objectName) =>
        $"Found '{objectName}' under table '{table}', but not inside display folder '{folder}': it is in another "
        + "folder or none. Object names are unique in a table, so the walk stopped there."
        + OrderOnly();

    private string LeftTable() =>
        (
            seenFolder
                ? $"Walked through display folder '{folder}' under table '{table}' without finding the object."
                : $"Walked through table '{table}' and found no display folder '{folder}' under it for this search."
        ) + OrderOnly();

    private string OrderOnly() =>
        indented == true ? "" : " Placement was checked by row order only: the tree did not show its indentation.";

    private static bool IsFolder(RowObservation row) =>
        row.Type is { } type
        && !Te3Page.ObjectTypeMatches("Column", type)
        && !Te3Page.ObjectTypeMatches("Measure", type)
        && !NotFolders.Contains(type.Replace(" ", "", StringComparison.Ordinal));
}
