/// <summary>One realized TOM Explorer row: its index in the tree (TreeItem <c>NodeN</c>), its text and its depth.</summary>
/// <param name="Page">The PageDown presses after Home at which the row was in view (0 for the first screen).</param>
public sealed record TreeRow(int Index, string? Value, int? Depth, int Page = 0);

/// <summary>The rows that can be the object, and whether a row of unknown depth broke the placement on the way.</summary>
public sealed record Pick(IReadOnlyList<TreeRow> Candidates, bool Unplaced);

/// <summary>
/// Picks the requested object among the rows the search filter realized, by names and depth alone (no row is
/// typed): Tables at depth 1, a table at depth 2 under it, the folder path at the table's depth + 1 + n, and the
/// object under them. A table, column or measure is never looked for under Roles or any other group, so a role's
/// table permission named like the table is never taken for it.
/// </summary>
internal static class SearchPick
{
    private const int TablesDepth = 1;
    private const int TableDepth = 2;

    /// <summary>
    /// The rows that can be the object, in tree order (a row named like the object at the exact depth the folder path
    /// gives, or any depth under the table when there is no folder): the caller selects each and lets Properties'
    /// Name and ObjectType decide. Empty when none; a row of unknown depth is never a candidate.
    /// </summary>
    /// <remarks>
    /// A row of unknown depth could be a table, so it ends the requested table: nothing after it is a candidate until
    /// the next row placed at table depth or above, and <see cref="Pick.Unplaced"/> tells the caller to fall back.
    /// </remarks>
    public static Pick Candidates(IReadOnlyList<TreeRow> rows, WalkRequest request)
    {
        var scan = new Scan(request);
        var unplaced = false;
        foreach (var row in rows)
        {
            if (row.Depth is not { } depth)
            {
                unplaced = true;
                scan.Break();
            }
            else if (scan.IsCandidate(row, depth))
            {
                scan.Found.Add(row);
            }
        }

        return new(scan.Found, unplaced);
    }

    // Where the scan is: the top-level group, whether it is inside the requested table, and how many folders of the
    // path it is inside.
    private sealed class Scan(WalkRequest request)
    {
        private readonly string[] segments = request.Folder.Split(
            '\\',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );

        private string? group;
        private bool inTable;
        private int matched;

        public List<TreeRow> Found { get; } = [];

        // A row that could not be placed: whatever follows may belong to another table.
        public void Break()
        {
            inTable = false;
            matched = 0;
        }

        public bool IsCandidate(TreeRow row, int depth)
        {
            if (depth <= TableDepth)
            {
                group = depth == TablesDepth ? row.Value : group;
                inTable = depth == TableDepth && group == WalkPlace.TablesGroup && row.Value == request.Table;
                matched = 0;
                return inTable && request.ObjectType == "Table";
            }

            if (!inTable || request.ObjectType == "Table")
            {
                return false;
            }

            // A row at depth d is inside at most d - table depth - 1 folders: a shallower row has left the deeper ones.
            matched = Math.Min(matched, depth - TableDepth - 1);
            if (matched < segments.Length && depth == TableDepth + 1 + matched && row.Value == segments[matched])
            {
                matched++;
                return false;
            }

            return row.Value == request.ObjectName && InPlace(depth);
        }

        // With a folder path, the object sits directly in its last folder; without one, anywhere under the table.
        private bool InPlace(int depth) =>
            segments.Length == 0 ? depth > TableDepth : matched == segments.Length && depth == TableDepth + 1 + matched;
    }
}
