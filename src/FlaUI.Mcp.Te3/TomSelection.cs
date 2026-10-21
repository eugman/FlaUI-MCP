/// <summary>
/// Reads TOM Explorer's selection from the status bar text, such as <c>Table "Brands"</c>, for the object walk.
/// </summary>
internal static class TomSelection
{
    /// <summary>The type prefix of a status-bar selection: <c>Measure "Gross Sales"</c> gives "Measure".</summary>
    internal static string? SelectionType(string? selection)
    {
        var quote = selection?.IndexOf('"', StringComparison.Ordinal) ?? -1;
        return quote > 0 ? selection![..quote].Trim() : null;
    }

    /// <summary>
    /// The quoted name of a status-bar selection when it is whole: <c>Table "Brands"</c> gives "Brands"; a truncated
    /// <c>Column "Calenc</c> gives null.
    /// </summary>
    internal static string? SelectionName(string? selection)
    {
        var open = selection?.IndexOf('"', StringComparison.Ordinal) ?? -1;
        var close = open >= 0 ? selection!.LastIndexOf('"') : -1;
        return close > open + 1 ? selection![(open + 1)..close] : null;
    }

    /// <summary>
    /// The selected row as the walk sees it. A selected row whose TreeItem gave no value (a table row read mid-scroll)
    /// takes its name from the status bar, so the table it opens is carried to the column, hierarchy and level rows
    /// below it instead of being lost as 'none'.
    /// </summary>
    internal static RowObservation ObservedRow(string? treeValue, string? bar, double? left = null) =>
        new(treeValue ?? SelectionName(bar), SelectionType(bar), left);

    /// <summary>
    /// True when a realized row's top lies below the selected row's: the selection had somewhere to move. A selection
    /// with no known position has none.
    /// </summary>
    internal static bool RowShownBelow(IEnumerable<double?> rowTops, double? selectedTop) =>
        selectedTop is { } top && rowTops.Any(other => other > top);
}
