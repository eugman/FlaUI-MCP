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
    /// <param name="view">The status bar's slot and Properties' rows for the selection, as they read now.</param>
    public static async Task<WalkResult> Find(
        ITreeCursor cursor,
        WalkRequest request,
        Func<bool> outOfTime,
        Func<bool> canRead,
        Func<SelectionView> view
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
        return await Arrived(cursor, walk, request, view, walked) ? walk with { Found = true } : walk;
    }

    /// <summary>
    /// Why the selection after the search cleared is not proven to be the object: with its table row out of view,
    /// what TE3 names is not the object as the rows read before the clear placed it; otherwise it is in another
    /// table, or not the object's row there.
    /// </summary>
    public static string ClearMiss(RowObservation after, SelectionPlace place, WalkRequest request, SelectionView seen)
    {
        var now = $"'{after.Value}' ({after.Type ?? "unknown type"})";
        if (place.Table == null)
        {
            var shown = seen.Folder == null ? "" : $" in display folder '{seen.Folder}'";
            var type = request.ObjectType.Length > 0 ? request.ObjectType : "any type";
            var folder = request.Folder.Length > 0 ? $" in folder '{request.Folder}'" : "";
            return $"After the search cleared, TE3 names the selection {now}{shown}, which is not "
                + $"'{request.ObjectName}' ({type}){folder} under table '{request.Table}' as the rows read before the "
                + "clear placed it (its table row is not in view). Observe the selection before continuing.";
        }

        return place.Table != request.Table
            ? $"Selection moved when the search was cleared: now {now} under table '{place.Table}'."
            : $"Selection moved when the search was cleared: now {now} under table '{place.Table}', not the "
                + "requested object's row in its folder.";
    }

    /// <summary>
    /// The selection is the object the walk reached: by identity (name and type, the cursor typing the row, and
    /// Properties' Name when shown) and by where it sits. The rows read now place it when its table row is in view
    /// (<paramref name="place"/>: the table from the nearest table row above it under Tables, and whether it is the
    /// object's row in that table and folder). When the search clears, TE3 scrolls the selected row to the top of the
    /// viewport, so the table row is above the realized rows: then the placement the rows read before the clear made
    /// (the walk's table, the object's row in its folder) stands, unless Properties' Display Folder, when it shows,
    /// names another folder. A walk that measured no table either (the already-selected shortcut) is confirmed only
    /// by Properties' Display Folder read now as the requested one. A walk that was not found never arrived.
    /// </summary>
    public static async Task<bool> Arrived(
        ITreeCursor cursor,
        WalkResult walk,
        WalkRequest request,
        Func<SelectionView> view,
        Func<Task<SelectionPlace>> place
    )
    {
        if (!(walk.Found || walk.Rescuable))
        {
            return false;
        }

        var row = await cursor.Current();
        var at = await place();
        var seen = view();
        var table = at.Table ?? walk.Table;
        var placed = at.Table != null ? at.Placed : InFolder(seen, request, measured: table != null);
        return placed
            && (table == null || table == request.Table)
            && PathWalker.Arrived(
                row,
                seen.Name,
                null,
                request.Table,
                request.Table,
                request.ObjectName,
                request.ObjectType
            );
    }

    // With the table measured before the clear, Properties' Display Folder may contradict the placement but need not
    // be read; with no table measured at all, only the folder read now as the requested one confirms the selection.
    private static bool InFolder(SelectionView seen, WalkRequest request, bool measured)
    {
        if (!measured)
        {
            return request.Folder.Length > 0 && seen.Folder == request.Folder;
        }

        return request.Folder.Length == 0 || seen.Folder == null || seen.Folder == request.Folder;
    }
}

/// <summary>Where the rows read place the selection: its table (null when no table row above it was read), and
/// whether it is a candidate for the request there (right name, table, folder and depth).</summary>
public sealed record SelectionPlace(string? Table, bool Placed);
