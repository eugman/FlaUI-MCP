using FlaUI.Mcp.Core;

/// <summary>
/// A TOM Explorer row's depth and type from what UIA shows (TE3 3.27.1, observed 2026-10-21). Every TreeItem
/// <c>NodeN</c> spans the whole row at the same x; its DataItem <c>Name row N</c> (same N, y and Value) starts where the
/// indented label starts, at TreeItem.x + 48 + 24 x depth, with the model root at depth 0.
/// </summary>
internal static class TomRows
{
    private const double RootIndent = 48;
    private const double Step = 24;
    private const double Slack = 2;

    /// <summary>The depth of a row whose TreeItem starts at <paramref name="tree"/> and label at <paramref name="label"/>.</summary>
    public static int? Depth(double tree, double label)
    {
        var steps = (label - tree - RootIndent) / Step;
        var depth = (int)Math.Round(steps);
        return depth >= 0 && Math.Abs(steps - depth) * Step <= Slack ? depth : null;
    }

    /// <summary>The selected TreeItem and its depth, from its DataItem with the same row index (or, failing that, y).</summary>
    public static (ElementInfo? Selected, int? Depth) Selected(IReadOnlyList<ElementInfo> elements)
    {
        var selected = elements.FirstOrDefault(e => e.ControlType == "TreeItem" && e.Selected == true);
        if (selected?.Bounds is not { } row)
        {
            return (selected, null);
        }

        var index = selected.Name.StartsWith("Node", StringComparison.Ordinal) ? selected.Name[4..] : null;
        var label =
            elements.FirstOrDefault(e => e.ControlType == "DataItem" && index != null && e.Name == "Name row " + index)
            ?? elements.FirstOrDefault(e => e.ControlType == "DataItem" && e.Bounds?.Y == row.Y);
        return (selected, label?.Bounds is { } cell ? Depth(row.X, cell.X) : null);
    }

    /// <summary>
    /// The row's type: the status bar's when its selection names this row, otherwise Properties' ObjectType. The bar
    /// keeps the previous object while a display folder is selected, so a bar naming another row is not this row's.
    /// </summary>
    /// <param name="value">The selected row's text.</param>
    /// <param name="bar">The status bar's selection slot, e.g. <c>Column "Brand"</c>.</param>
    /// <param name="properties">Reads Properties' ObjectType, e.g. "Folder"; null when it cannot be read.</param>
    public static string? Type(string? value, string? bar, Func<string?> properties) =>
        value != null && TomSelection.SelectionName(bar) == value ? TomSelection.SelectionType(bar) : properties();
}
