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

    /// <summary>
    /// Where the selection is once the search has cleared, from the status bar's slot, against the row the walk
    /// ended on (<paramref name="endedOn"/>; null when that read failed). The walk's own Ctrl+Home and PageDowns leave the
    /// selection on another row than before it (te3-eb2ed6a: a folder miss ended on Products' row, 0 selects, and the
    /// bar read Table "Products" after the clear), so the reply names where it is and that no select put it there.
    /// </summary>
    public static string AfterClear(string? bar, string? endedOn)
    {
        if (bar == null)
        {
            return " After the search cleared, the status bar names no selection.";
        }

        if (endedOn == null)
        {
            return $" After the search cleared, the selection is {bar}; where the walk ended was not read, so whether "
                + "that is the same row is not known.";
        }

        return TomSelection.SelectionName(bar) == endedOn
            ? $" After the search cleared, the selection stayed on {bar}."
            : $" After the search cleared, the selection is {bar}, not the row the walk ended on ('{endedOn}'); the "
                + "walk sent no select for it.";
    }
}
