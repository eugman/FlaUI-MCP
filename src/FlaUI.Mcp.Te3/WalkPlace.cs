/// <summary>
/// Where an object walk is in the filtered tree, placed by each row's depth (<see cref="TomRows"/>): the top-level
/// group (depth 1: Tables, Roles, ...), the table the rows belong to, and which folders of the requested display folder
/// path the walk is inside. A row at or above a folder's depth has left that folder.
/// </summary>
/// <remarks>
/// Only a table row under Tables is a table: the search also matches role table permissions under Roles, which sit at
/// depth 3 like a folder. With a folder requested, a row whose depth could not be read is never placed inside it.
/// </remarks>
internal sealed class WalkPlace(string table, string? folder)
{
    public const string TablesGroup = "Tables";

    // Rows of these types are objects, not display folders, even when one shares a folder's name.
    private static readonly string[] NotFolders =
    [
        "Hierarchy",
        "Level",
        "Partition",
        "Relationship",
        "TablePermission",
    ];

    private readonly string[] segments = (folder ?? "").Split(
        '\\',
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
    );

    // The depths of the path's folders the walk is inside, innermost on top.
    private readonly Stack<int> inside = new();
    private string? group;
    private bool seenFolder;
    private bool placed = true;
    private int? tableDepth;

    /// <summary>The table the current row belongs to, or null before the first table row or outside Tables.</summary>
    public string? Table { get; private set; }

    /// <summary>True when no folder was asked for, or the current row is inside every folder of the path.</summary>
    public bool InFolder => !HasFolder || (inside.Count == segments.Length && placed);

    public bool HasFolder => segments.Length > 0;

    /// <summary>A top-level group other than Tables, whose subtree a table, column or measure walk skips.</summary>
    public static bool SkipsGroup(RowObservation row) => row.Depth == 1 && row.Value != TablesGroup;

    /// <summary>
    /// Moves the walk onto <paramref name="row"/>. Returns why the object cannot be further on when a folder was asked
    /// for and this row leaves the requested table; otherwise null.
    /// </summary>
    public string? Enter(RowObservation row)
    {
        var isTable = PathWalker.IsTable(row.Type) && InTables;
        // A row back at the table's own depth (or above) has left the table, typed or not.
        var leaves = Table != null && row.Depth is { } depth && tableDepth is { } own && depth <= own;
        if (row.Depth == 1 || isTable || leaves)
        {
            var left = HasFolder && Table == table ? LeftTable() : null;
            group = row.Depth == 1 ? row.Value : group;
            Table = isTable ? row.Value : null;
            tableDepth = isTable ? row.Depth : null;
            inside.Clear();
            return left;
        }

        if (Table == table && HasFolder)
        {
            Track(row);
        }

        return null;
    }

    private bool InTables => group is null or TablesGroup;

    // The path's folders are matched by name and exact depth: segment n sits at the table's depth + 1 + n. Rows off
    // the path are left untyped by the walk, so an outer folder may not be known as one; the exact depth is what
    // keeps a same-named folder nested inside it (X\A) from being taken for the requested top-level one (A).
    private void Track(RowObservation row)
    {
        placed = row.Depth != null;
        while (inside.Count > 0 && row.Depth <= inside.Peek())
        {
            inside.Pop();
        }

        if (NextOnPath(row) is { } depth)
        {
            inside.Push(depth);
            seenFolder = true;
        }
    }

    // The row's depth when it is the path's next folder: the next segment's name, a folder, at exactly that depth.
    private int? NextOnPath(RowObservation row)
    {
        if (row.Depth is not { } depth || tableDepth is not { } own || inside.Count == segments.Length)
        {
            return null;
        }

        return depth == own + 1 + inside.Count && IsFolder(row) && row.Value == segments[inside.Count] ? depth : null;
    }

    /// <summary>The object was found under the table, but not inside the folder path.</summary>
    public string OutsideFolder(string objectName) =>
        placed
            ? $"Found '{objectName}' under table '{table}', but not inside display folder '{folder}': it is in another "
                + "folder or none. Object names are unique in a table, so the walk stopped there."
            : $"Found '{objectName}' under table '{table}', but its row's depth could not be read, so whether it is "
                + $"inside display folder '{folder}' is not known.";

    private string LeftTable() =>
        seenFolder
            ? $"Walked through display folder '{folder}' under table '{table}' without finding the object."
            : $"Walked through table '{table}' and found no display folder '{folder}' under it for this search.";

    private static bool IsFolder(RowObservation row) =>
        row.Type is { } type
        && !IsObject(type)
        && !NotFolders.Contains(type.Replace(" ", "", StringComparison.Ordinal));

    private static bool IsObject(string type) =>
        Te3Page.ObjectTypeMatches("Column", type)
        || Te3Page.ObjectTypeMatches("Measure", type)
        || Te3Page.ObjectTypeMatches("Table", type);
}
