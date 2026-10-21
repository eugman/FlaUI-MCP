using System.Drawing;
using FlaUI.Mcp.Core;

/// <summary>
/// Where to drag to resize a docked pane. Dock splitters are invisible to UI Automation, so the
/// splitter is taken to be the gap between the pane and the neighbour that shares its edge.
/// </summary>
public static class SplitterPlan
{
    // Widest gap still treated as a shared edge; dock splitters are a few pixels, scaled by DPI.
    public const int MaxGap = 24;

    /// <summary>Drag start and end, in screen pixels, that make <paramref name="pane"/> the requested size.</summary>
    /// <param name="documents">The neighbours that are document areas: a dock pane touching one keeps the splitter.</param>
    /// <summary>The one size a resize asks for; refused before anything else when it names neither or both.</summary>
    public static int RequireOne(int? width, int? height) =>
        width is null == height is null
            ? throw new ArgumentException("Pass exactly one of width or height; no input sent.")
            : (width ?? height)!.Value;

    public static (Point From, Point To) Resize(
        Rectangle pane,
        IEnumerable<Rectangle> others,
        int? width,
        int? height,
        Rectangle? window = null,
        IEnumerable<Rectangle>? documents = null
    )
    {
        RequireOne(width, height);
        var neighbours = others.Where(o => o != pane).ToList();
        var docs = (documents ?? []).ToHashSet();
        (Point, Point)? Plan(List<Rectangle> candidates) =>
            width is { } w ? Horizontal(pane, candidates, w, docs) : Vertical(pane, candidates, height!.Value, docs);

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

    private static (Point, Point)? Horizontal(
        Rectangle pane,
        List<Rectangle> others,
        int width,
        HashSet<Rectangle> documents
    )
    {
        var y = (pane.Top + pane.Bottom) / 2;
        if (
            others.FirstOrDefault(o => Near(o.Right, pane.Left) && Spans(o.Top, o.Bottom, y)) is { IsEmpty: false } left
        )
        {
            var x = Between(left.Right, pane.Left, intoSecond: documents.Contains(left));
            return (new Point(x, y), new Point(x + pane.Width - width, y));
        }

        if (
            others.FirstOrDefault(o => Near(pane.Right, o.Left) && Spans(o.Top, o.Bottom, y)) is
            { IsEmpty: false } right
        )
        {
            var x = Between(pane.Right, right.Left, intoSecond: false);
            return (new Point(x, y), new Point(x + width - pane.Width, y));
        }

        return null;
    }

    private static (Point, Point)? Vertical(
        Rectangle pane,
        List<Rectangle> others,
        int height,
        HashSet<Rectangle> documents
    )
    {
        var x = (pane.Left + pane.Right) / 2;
        if (
            others.FirstOrDefault(o => Near(o.Bottom, pane.Top) && Spans(o.Left, o.Right, x)) is
            { IsEmpty: false } above
        )
        {
            var y = Between(above.Bottom, pane.Top, intoSecond: documents.Contains(above));
            return (new Point(x, y), new Point(x, y + pane.Height - height));
        }

        if (
            others.FirstOrDefault(o => Near(pane.Bottom, o.Top) && Spans(o.Left, o.Right, x)) is
            { IsEmpty: false } below
        )
        {
            var y = Between(pane.Bottom, below.Top, intoSecond: false);
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

    /// <summary>Pixels inside the pane that holds the splitter when two panes touch.</summary>
    /// <remarks>Seen live on 3.27.1 at 125%: between two dock panes the splitter is in the first pane's last rows,
    /// and a drag on the shared edge itself grabs the second pane's title bar and undocks it. Beside a document the
    /// splitter is the dock pane's border: a drag 3 px inside AI Assistant (left of the document) moved it, and one
    /// 3 px inside the document (left of TOM Explorer) moved nothing.</remarks>
    public const int TouchingInset = 3;

    /// <summary>A gap this narrow is handled as touching: its midpoint is the shared edge itself.</summary>
    public const int TouchingGap = 2;

    // The resized pane is never a document, so only a first neighbour (left or above) can send the inset into the second.
    private static int Between(int firstEnd, int secondStart, bool intoSecond)
    {
        if (secondStart - firstEnd > TouchingGap)
        {
            return (firstEnd + secondStart) / 2;
        }

        return intoSecond ? secondStart + TouchingInset : firstEnd - TouchingInset;
    }

    /// <summary>Which edge of <paramref name="pane"/> a drag starting at <paramref name="from"/> moves.</summary>
    public static string Edge(Rectangle pane, Point from, bool width)
    {
        if (width)
        {
            return from.X < (pane.Left + pane.Right) / 2 ? "left" : "right";
        }

        return from.Y < (pane.Top + pane.Bottom) / 2 ? "top" : "bottom";
    }

    /// <summary>Drags per resize: one, then a measured check, so a missed splitter costs one drag's input.</summary>
    public const int MaxAttempts = 1;

    /// <summary>The failure for a drag that left the pane short of its size: what was dragged and what it reached.</summary>
    /// <param name="from">Drag start in window pixels, as sent.</param>
    /// <param name="to">Drag end in window pixels, as sent.</param>
    public static string Missed(
        string title,
        string edge,
        Point from,
        Point to,
        int requested,
        int reached,
        int attempts
    ) =>
        $"Resize of '{title}' did not reach the requested size: dragged its {edge} edge from ({from.X},{from.Y}) to "
        + $"({to.X},{to.Y}) in window pixels, requested {requested}, reached {reached} after {attempts} attempt"
        + $"{(attempts == 1 ? "" : "s")}; the drag may have missed the splitter. Inspect before retrying.";

    /// <summary>
    /// Fails when the drag opened a new titled top-level window in the process: the drag grabbed a pane's title bar
    /// and floated it instead of moving the splitter.
    /// </summary>
    /// <param name="before">The process's top-level window handles before the drag.</param>
    /// <param name="after">The process's top-level windows after the drag.</param>
    /// <param name="title">The pane being resized.</param>
    public static void ThrowIfUndocked(IEnumerable<nint> before, IEnumerable<Win32WindowInfo> after, string title)
    {
        var known = before.ToHashSet();
        if (after.FirstOrDefault(w => !known.Contains(w.Hwnd) && w.Title.Length > 0) is { } floating)
        {
            throw new InvalidOperationException(
                $"Resize of '{title}' undocked: true; the drag grabbed a title bar and TE3 opened floating window "
                    + $"'{floating.Title}'. Next: dock the pane again by hand, check te3_layout_read list, then retry."
            );
        }
    }

    private static bool Near(int edge, int other) => other - edge is >= 0 and <= MaxGap;

    private static bool Spans(int start, int end, int at) => at > start && at < end;
}
