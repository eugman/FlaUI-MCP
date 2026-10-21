/// <summary>te3_navigate object's request: the object, its table and, when given, its display folder path.</summary>
public sealed record WalkRequest(string Table, string ObjectName, string ObjectType, string Folder = "");

/// <summary>
/// The decisions around the TOM Explorer walk, apart from the UIA reads so they can be tested over a scripted tree:
/// the walk itself, the one rescue of a stop the walk did not decide, and the check after the search is cleared.
/// </summary>
internal static class ObjectWalk
{
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

        return await Arrived(cursor, walk, request, propertiesName) ? walk with { Found = true } : walk;
    }

    /// <summary>
    /// The selection is the object the walk reached: name and type (the cursor types the row), the table the walk
    /// passed and, when shown, Properties' Name. A walk that was not found, or not inside the folder, never arrived.
    /// </summary>
    public static async Task<bool> Arrived(
        ITreeCursor cursor,
        WalkResult walk,
        WalkRequest request,
        Func<string?> propertiesName
    )
    {
        if (!(walk.Found || walk.Rescuable))
        {
            return false;
        }

        var row = await cursor.Current();
        return row.Depth != null || request.Folder.Length == 0
            ? PathWalker.Arrived(
                row,
                propertiesName(),
                null,
                walk.Table,
                request.Table,
                request.ObjectName,
                request.ObjectType
            )
            : false;
    }
}
