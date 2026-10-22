/// <summary>te3_navigate object's request: the object, its table and, when given, its display folder path.</summary>
public sealed record WalkRequest(string Table, string ObjectName, string ObjectType, string Folder = "");

/// <summary>
/// The decisions around the TOM Explorer walk, apart from the UIA reads so they can be tested over a scripted tree:
/// the walk itself, the one rescue of a stop the walk did not decide, and the check after the search is cleared.
/// </summary>
internal static class ObjectWalk
{
    /// <summary>
    /// The row texts whose type the walk needs: the object, its table and each folder of the path. Any other row is
    /// placed by its depth alone, which spares a Properties read for it.
    /// </summary>
    public static Func<string?, bool> Interesting(WalkRequest request)
    {
        var names = new HashSet<string>(
            request.Folder.Split('\\', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        )
        {
            request.ObjectName,
            request.Table,
        };
        return value => value != null && names.Contains(value);
    }

    /// <summary>
    /// Walks the tree. A stop the walk did not decide (<see cref="WalkResult.Rescuable"/>) is rescued only when time is
    /// left for the read and the selection is the object; a miss the walk decided is never turned into arrival.
    /// </summary>
    /// <param name="outOfTime">True once no further key may be pressed.</param>
    /// <param name="canRead">True while one more read of the selection still fits in the tool's time.</param>
    /// <param name="propertiesName">Properties' Name for the selection, or null when it cannot be read.</param>
    public static async Task<WalkResult> Find(
        ITreeCursor cursor,
        WalkRequest request,
        Func<bool> outOfTime,
        Func<bool> canRead,
        Func<string?> propertiesName
    )
    {
        var walk = await PathWalker.Find(
            cursor,
            request.Table,
            request.ObjectName,
            request.ObjectType,
            outOfTime: outOfTime,
            folder: request.Folder
        );
        if (walk.Found || !walk.Rescuable || !canRead())
        {
            return walk;
        }

        // A rescue happens before the search clears, on the row the walk itself read its way to: the table and folder
        // are the ones the walk passed through (Rescuable requires the row inside the folder).
        var walked = () => Task.FromResult(new SelectionPlace(walk.Table, true));
        return await Arrived(cursor, walk, request, propertiesName, walked) ? walk with { Found = true } : walk;
    }

    /// <summary>
    /// The selection is the object the walk reached: by identity (name and type, the cursor typing the row, and
    /// Properties' Name when shown) and by where the rows read now place it (<paramref name="place"/>: the table from
    /// the nearest table row above it under Tables, and whether it is the object's row in that table and folder), not
    /// by its row index or the walk's own table. A walk that was not found, or rows that cannot place the selection,
    /// never arrived.
    /// </summary>
    public static async Task<bool> Arrived(
        ITreeCursor cursor,
        WalkResult walk,
        WalkRequest request,
        Func<string?> propertiesName,
        Func<Task<SelectionPlace>> place
    )
    {
        if (!(walk.Found || walk.Rescuable))
        {
            return false;
        }

        var row = await cursor.Current();
        var at = await place();
        return at.Placed
            && PathWalker.Arrived(
                row,
                propertiesName(),
                null,
                at.Table,
                request.Table,
                request.ObjectName,
                request.ObjectType
            );
    }
}

/// <summary>Where the rows read place the selection: its table (null when no table row above it was read), and
/// whether it is a candidate for the request there (right name, table, folder and depth).</summary>
public sealed record SelectionPlace(string? Table, bool Placed);
