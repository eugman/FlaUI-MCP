using System.Drawing;

/// <summary>
/// Where to drag to resize a docked pane. Dock splitters are invisible to UI Automation, so the
/// splitter is taken to be the gap between the pane and the neighbour that shares its edge.
/// </summary>
public static class SplitterPlan
{
    // Widest gap still treated as a shared edge; dock splitters are a few pixels, scaled by DPI.
    public const int MaxGap = 24;

    /// <summary>Drag start and end, in screen pixels, that make <paramref name="pane"/> the requested size.</summary>
    public static (Point From, Point To) Resize(Rectangle pane, IEnumerable<Rectangle> others, int? width, int? height)
    {
        if (width is null == height is null)
        {
            throw new ArgumentException("Pass exactly one of width or height.");
        }

        var neighbours = others.Where(o => o != pane).ToList();
        return width is { } w ? Horizontal(pane, neighbours, w) : Vertical(pane, neighbours, height!.Value);
    }

    private static (Point, Point) Horizontal(Rectangle pane, List<Rectangle> others, int width)
    {
        var y = (pane.Top + pane.Bottom) / 2;
        if (
            others.FirstOrDefault(o => Near(o.Right, pane.Left) && Spans(o.Top, o.Bottom, y)) is { IsEmpty: false } left
        )
        {
            var x = (left.Right + pane.Left) / 2;
            return (new Point(x, y), new Point(x + pane.Width - width, y));
        }

        if (
            others.FirstOrDefault(o => Near(pane.Right, o.Left) && Spans(o.Top, o.Bottom, y)) is
            { IsEmpty: false } right
        )
        {
            var x = (pane.Right + right.Left) / 2;
            return (new Point(x, y), new Point(x + width - pane.Width, y));
        }

        throw new InvalidOperationException("No pane shares a vertical edge with this one; nothing to drag.");
    }

    private static (Point, Point) Vertical(Rectangle pane, List<Rectangle> others, int height)
    {
        var x = (pane.Left + pane.Right) / 2;
        if (
            others.FirstOrDefault(o => Near(o.Bottom, pane.Top) && Spans(o.Left, o.Right, x)) is
            { IsEmpty: false } above
        )
        {
            var y = (above.Bottom + pane.Top) / 2;
            return (new Point(x, y), new Point(x, y + pane.Height - height));
        }

        if (
            others.FirstOrDefault(o => Near(pane.Bottom, o.Top) && Spans(o.Left, o.Right, x)) is
            { IsEmpty: false } below
        )
        {
            var y = (pane.Bottom + below.Top) / 2;
            return (new Point(x, y), new Point(x, y + height - pane.Height));
        }

        throw new InvalidOperationException("No pane shares a horizontal edge with this one; nothing to drag.");
    }

    private static bool Near(int edge, int other) => other - edge is >= 0 and <= MaxGap;

    private static bool Spans(int start, int end, int at) => at > start && at < end;
}
