/// <summary>
/// The text of an object navigation that did not arrive: what was asked for (its type, or "object"), what the walk
/// says, and where the selection actually ended, with its table taken from the rows read (the nearest table row above
/// it), never from walk state that may have lost it.
/// </summary>
internal static class WalkMiss
{
    public static string Text(WalkRequest request, string? reason, TreeRow? endedOn, string? endedTable, int inputs)
    {
        var what = request.ObjectType.Length > 0 ? request.ObjectType : "object";
        var where = request.Folder.Length > 0 ? $" in folder '{request.Folder}'" : "";
        var under = endedTable == null ? " (no table row above it was read)." : $" under table '{endedTable}'.";
        var ended =
            endedOn == null
                ? "Where the selection ended could not be read."
                : $"The selection ended on row {endedOn.Index} '{endedOn.Value}'{under}";
        return $"No {what} '{request.ObjectName}' under table '{request.Table}'{where} in TOM Explorer "
            + $"({inputs} inputs). {reason} {ended}";
    }
}
