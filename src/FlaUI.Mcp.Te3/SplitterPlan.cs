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
    public static (Point From, Point To) Resize(
        Rectangle pane,
        IEnumerable<Rectangle> others,
        int? width,
        int? height,
        Rectangle? window = null
    )
    {
        if (width is null == height is null)
        {
            throw new ArgumentException("Pass exactly one of width or height.");
        }

        var neighbours = others.Where(o => o != pane).ToList();
        (Point, Point)? Plan(List<Rectangle> candidates) =>
            width is { } w ? Horizontal(pane, candidates, w) : Vertical(pane, candidates, height!.Value);

        // A real neighbour on either side wins; the interior fallback is only for an edge with none.
        if (Plan(neighbours) is { } shared)
        {
            return shared;
        }

        // An empty document area is no pane, so it leaves no neighbour. Its splitter is still there,
        // just outside the pane edge that faces the window interior.
        var room = window ?? Rectangle.Empty;
        return (room.IsEmpty ? null : Plan(InteriorGaps(pane, room).ToList()))
            ?? throw new InvalidOperationException(
                $"No pane shares a {(width is null ? "horizontal" : "vertical")} edge with this one; nothing to drag."
            );
    }

    private static (Point, Point)? Horizontal(Rectangle pane, List<Rectangle> others, int width)
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

        return null;
    }

    private static (Point, Point)? Vertical(Rectangle pane, List<Rectangle> others, int height)
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

        return null;
    }

    // Pixels between a pane edge and the dock splitter beside it, seen live at 175%.
    public const int SplitterOffset = 5;

    /// <summary>Stand-in neighbours for the window space beyond each pane edge that faces the interior.</summary>
    private static IEnumerable<Rectangle> InteriorGaps(Rectangle pane, Rectangle window)
    {
        if (pane.Left - window.Left > MaxGap)
        {
            yield return Rectangle.FromLTRB(window.Left, pane.Top, pane.Left - 2 * SplitterOffset, pane.Bottom);
        }

        if (window.Right - pane.Right > MaxGap)
        {
            yield return Rectangle.FromLTRB(pane.Right + 2 * SplitterOffset, pane.Top, window.Right, pane.Bottom);
        }

        if (pane.Top - window.Top > MaxGap)
        {
            yield return Rectangle.FromLTRB(pane.Left, window.Top, pane.Right, pane.Top - 2 * SplitterOffset);
        }

        if (window.Bottom - pane.Bottom > MaxGap)
        {
            yield return Rectangle.FromLTRB(pane.Left, pane.Bottom + 2 * SplitterOffset, pane.Right, window.Bottom);
        }
    }

    private static bool Near(int edge, int other) => other - edge is >= 0 and <= MaxGap;

    private static bool Spans(int start, int end, int at) => at > start && at < end;
}
