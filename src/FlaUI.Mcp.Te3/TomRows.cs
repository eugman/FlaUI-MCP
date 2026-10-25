using FlaUI.Mcp.Core;

/// <summary>
/// A TOM Explorer row's depth and type from what UIA shows (TE3 3.27.1, observed 2026-10-21). Every TreeItem
/// <c>NodeN</c> spans the whole row at the same x; its DataItem <c>Name row N</c> (same N, y and Value) starts where the
/// indented label starts, at TreeItem.x + indent + step x depth, with the model root at depth 0 (see <see cref="Scale"/>).
/// </summary>
internal static class TomRows
{
    private const double Slack = 2;

    /// <summary>
    /// How far the labels indent: the model root's label sits <paramref name="RootIndent"/> px right of its TreeItem,
    /// and each level adds <paramref name="Step"/> px. Measured at runtime (48 and 24 at 125%), never assumed.
    /// </summary>
    public sealed record Scale(double RootIndent, double Step);

    /// <summary>
    /// The scale from the model root (row 0, depth 0) and its first child (row 1, depth 1) when both are realized;
    /// null otherwise. A visible row named Tables is not independent evidence of the group: a model table or
    /// display folder can have that name too. Callers with a confirmed anchor may use CalibrateFrom.
    /// </summary>
    public static Scale? Calibrate(IReadOnlyList<ElementInfo> elements) => CalibrateFrom(elements, 0, 0);

    /// <summary>
    /// Keep a scale measured from the unfiltered tree during this navigation when a filter hides its root. If the
    /// filtered tree still realizes the root and its first child, their fresh measurement takes precedence.
    /// </summary>
    public static Scale? FilteredScale(IReadOnlyList<ElementInfo> elements, Scale? unfiltered) =>
        Calibrate(elements) ?? unfiltered;

    /// <summary>After Home, wait briefly for the virtualized root and first child to be realized twice.</summary>
    public static async Task<Scale?> WaitForRootScale(
        Func<Task<IReadOnlyList<ElementInfo>>> read,
        Func<int, Task> delay,
        Func<bool>? outOfTime = null
    ) => await RowSettle.WaitStable(async () => Calibrate(await read()), delay, budgetMs: 900, pollMs: 150, outOfTime);

    /// <summary>
    /// The scale from the realized row <paramref name="index"/> of known <paramref name="depth"/> (the model root at
    /// 0, a selected table at 2) and the row right below it, taken as its first child one level deeper; null when a
    /// label is missing or the row below is not deeper (the row is collapsed, or a leaf).
    /// </summary>
    public static Scale? CalibrateFrom(IReadOnlyList<ElementInfo> elements, int index, int depth)
    {
        var tree = elements.FirstOrDefault(e => e.ControlType == "TreeItem" && e.Name == "Node" + index)?.Bounds;
        var label = Label(elements, index.ToString())?.Bounds;
        var child = Label(elements, (index + 1).ToString())?.Bounds;
        if (tree == null || label == null || child == null)
        {
            return null;
        }

        var step = child.X - label.X;
        var indent = label.X - tree.X - step * depth;
        return step > Slack && indent > Slack ? new(indent, step) : null;
    }

    /// <summary>The depth of a row whose TreeItem starts at <paramref name="tree"/> and label at <paramref name="label"/>.</summary>
    public static int? Depth(double tree, double label, Scale? scale)
    {
        if (scale == null)
        {
            return null;
        }

        var steps = (label - tree - scale.RootIndent) / scale.Step;
        var depth = (int)Math.Round(steps);
        return depth >= 0 && Math.Abs(steps - depth) * scale.Step <= Slack ? depth : null;
    }

    /// <summary>The selected TreeItem and its depth, from its DataItem with the same row index (or, failing that, y).</summary>
    public static (ElementInfo? Selected, int? Depth) Selected(IReadOnlyList<ElementInfo> elements, Scale? scale)
    {
        var selected = elements.FirstOrDefault(e => e.ControlType == "TreeItem" && e.Selected == true);
        if (selected?.Bounds is not { } row)
        {
            return (selected, null);
        }

        var index = selected.Name.StartsWith("Node", StringComparison.Ordinal) ? selected.Name[4..] : null;
        var label =
            (index == null ? null : Label(elements, index))
            ?? elements.FirstOrDefault(e => e.ControlType == "DataItem" && e.Bounds?.Y == row.Y);
        return (selected, label?.Bounds is { } cell ? Depth(row.X, cell.X, scale) : null);
    }

    /// <summary>
    /// Whether the row shown just below the selected one is deeper: the selection is open, so Left collapses it rather
    /// than moving to its parent. False when either depth is unknown.
    /// </summary>
    public static bool ChildBelow(IReadOnlyList<ElementInfo> elements, Scale? scale)
    {
        var (selected, depth) = Selected(elements, scale);
        var below = elements
            .Where(e => e.ControlType == "TreeItem" && e.Bounds?.Y > selected?.Bounds?.Y)
            .MinBy(e => e.Bounds!.Y);
        var label = below == null ? null : Label(elements, below.Name.Replace("Node", "", StringComparison.Ordinal));
        var belowDepth = below?.Bounds is { } row && label?.Bounds is { } cell ? Depth(row.X, cell.X, scale) : null;
        return depth != null && belowDepth > depth;
    }

    /// <summary>Every realized row, in tree order (by y): its NodeN index, its text and its depth.</summary>
    public static IReadOnlyList<TreeRow> Rows(IReadOnlyList<ElementInfo> elements, Scale? scale) =>
        [
            .. elements
                .Where(e => e.ControlType == "TreeItem" && e.Name.StartsWith("Node", StringComparison.Ordinal))
                .OrderBy(e => e.Bounds?.Y ?? double.MaxValue)
                .Select(e =>
                {
                    var index = e.Name[4..];
                    var label = Label(elements, index);
                    var depth = e.Bounds is { } row && label?.Bounds is { } cell ? Depth(row.X, cell.X, scale) : null;
                    return new TreeRow(int.TryParse(index, out var n) ? n : -1, e.Value, depth);
                }),
        ];

    /// <summary>
    /// Whether the tree shows a vertical scroll bar, so rows past the viewport may not be realized. treeList offers
    /// no Scroll pattern (3.27.1), so the caller then falls back to the keyboard walk.
    /// </summary>
    public static bool Scrolls(IReadOnlyList<ElementInfo> elements) =>
        elements.Any(e => e.ControlType == "ScrollBar" && e.Name == "Vertical" && !e.Offscreen);

    private static ElementInfo? Label(IReadOnlyList<ElementInfo> elements, string index) =>
        elements.FirstOrDefault(e => e.ControlType == "DataItem" && e.Name == "Name row " + index);

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
