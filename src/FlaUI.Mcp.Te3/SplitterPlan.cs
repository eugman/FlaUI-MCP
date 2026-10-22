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

    /// <summary>The one size a resize asks for; refused before anything else when it names neither or both.</summary>
    public static int RequireOne(int? width, int? height) =>
        width is null == height is null
            ? throw new ArgumentException("Pass exactly one of width or height; no input sent.")
            : (width ?? height)!.Value;

    /// <summary>Drag start and end, in screen pixels, that make <paramref name="pane"/> the requested size.</summary>
    /// <param name="documents">The neighbours that are document areas: a dock pane touching one keeps the splitter.</param>
    /// <param name="dpi">The window's DPI, which scales the pixels inside a pane edge (see <see cref="TouchingInset"/>).</param>
    public static (Point From, Point To) Resize(
        Rectangle pane,
        IEnumerable<Rectangle> others,
        int? width,
        int? height,
        Rectangle? window = null,
        IEnumerable<Rectangle>? documents = null,
        uint dpi = BaseDpi
    )
    {
        RequireOne(width, height);
        var neighbours = others.Where(o => o != pane).ToList();
        var docs = (documents ?? []).ToHashSet();
        var inset = TouchingInset(dpi);
        (Point, Point)? Plan(List<Rectangle> candidates) =>
            width is { } w
                ? Horizontal(pane, candidates, w, docs, inset)
                : Vertical(pane, candidates, height!.Value, docs, inset);

        // A real neighbour on either side wins; the interior fallback is only for an edge with none.
        if (Plan(neighbours) is { } shared)
        {
            return shared;
        }

        // An empty document area is no pane, so it leaves no neighbour. Its splitter is still there,
        // just outside the pane edge that faces the window interior.
        var room = window ?? Rectangle.Empty;
        return (room.IsEmpty ? null : Plan(InteriorGaps(pane, room, SplitterOffset(dpi)).ToList()))
            ?? throw new InvalidOperationException(
                $"No pane shares a {(width is null ? "horizontal" : "vertical")} edge with this one; nothing to drag."
            );
    }

    private static (Point, Point)? Horizontal(
        Rectangle pane,
        List<Rectangle> others,
        int width,
        HashSet<Rectangle> documents,
        int inset
    )
    {
        var y = (pane.Top + pane.Bottom) / 2;
        if (
            others.FirstOrDefault(o => Near(o.Right, pane.Left) && Spans(o.Top, o.Bottom, y)) is { IsEmpty: false } left
        )
        {
            var x = Between(left.Right, pane.Left, documents.Contains(left), inset);
            return (new Point(x, y), new Point(x + pane.Width - width, y));
        }

        if (
            others.FirstOrDefault(o => Near(pane.Right, o.Left) && Spans(o.Top, o.Bottom, y)) is
            { IsEmpty: false } right
        )
        {
            var x = Between(pane.Right, right.Left, false, inset);
            return (new Point(x, y), new Point(x + width - pane.Width, y));
        }

        return null;
    }

    private static (Point, Point)? Vertical(
        Rectangle pane,
        List<Rectangle> others,
        int height,
        HashSet<Rectangle> documents,
        int inset
    )
    {
        var x = (pane.Left + pane.Right) / 2;
        if (
            others.FirstOrDefault(o => Near(o.Bottom, pane.Top) && Spans(o.Left, o.Right, x)) is
            { IsEmpty: false } above
        )
        {
            var y = Between(above.Bottom, pane.Top, documents.Contains(above), inset);
            return (new Point(x, y), new Point(x, y + pane.Height - height));
        }

        if (
            others.FirstOrDefault(o => Near(pane.Bottom, o.Top) && Spans(o.Left, o.Right, x)) is
            { IsEmpty: false } below
        )
        {
            var y = Between(pane.Bottom, below.Top, false, inset);
            return (new Point(x, y), new Point(x, y + height - pane.Height));
        }

        return null;
    }

    /// <summary>The DPI the device-independent pixels below are given at.</summary>
    public const uint BaseDpi = 96;

    /// <summary>Device-independent pixels between a pane edge and the dock splitter beside it: 5 px seen live at 175%.</summary>
    public const int SplitterOffsetDip = 3;

    /// <summary>Pixels between a pane edge and the dock splitter beside it at the window's DPI (5 at 175%, 4 at 125%).</summary>
    public static int SplitterOffset(uint dpi) => Scaled(SplitterOffsetDip, dpi);

    /// <summary>Stand-in neighbours for the window space beyond each pane edge that faces the interior.</summary>
    private static IEnumerable<Rectangle> InteriorGaps(Rectangle pane, Rectangle window, int offset)
    {
        if (pane.Left - window.Left > MaxGap)
        {
            yield return Rectangle.FromLTRB(window.Left, pane.Top, pane.Left - 2 * offset, pane.Bottom);
        }

        if (window.Right - pane.Right > MaxGap)
        {
            yield return Rectangle.FromLTRB(pane.Right + 2 * offset, pane.Top, window.Right, pane.Bottom);
        }

        if (pane.Top - window.Top > MaxGap)
        {
            yield return Rectangle.FromLTRB(pane.Left, window.Top, pane.Right, pane.Top - 2 * offset);
        }

        if (window.Bottom - pane.Bottom > MaxGap)
        {
            yield return Rectangle.FromLTRB(pane.Left, pane.Bottom + 2 * offset, pane.Right, window.Bottom);
        }
    }

    /// <summary>Device-independent pixels inside the pane that holds the splitter when two panes touch.</summary>
    /// <remarks>Seen live on 3.27.1 at 125% as 3 px: between two dock panes the splitter is in the first pane's last
    /// rows, and a drag on the shared edge itself grabs the second pane's title bar and undocks it. Beside a document
    /// the splitter is the dock pane's border: a drag 3 px inside AI Assistant (left of the document) moved it, and
    /// one 3 px inside the document (left of TOM Explorer) moved nothing.</remarks>
    public const int TouchingInsetDip = 2;

    /// <summary>Pixels inside the pane that holds the splitter at the window's DPI (3 at 125%, 4 at 175%).</summary>
    public static int TouchingInset(uint dpi) => Scaled(TouchingInsetDip, dpi);

    private static int Scaled(int dip, uint dpi) =>
        (int)Math.Round(dip * dpi / (double)BaseDpi, MidpointRounding.AwayFromZero);

    /// <summary>A gap this narrow is handled as touching: its midpoint is the shared edge itself.</summary>
    public const int TouchingGap = 2;

    // The resized pane is never a document, so only a first neighbour (left or above) can send the inset into the second.
    private static int Between(int firstEnd, int secondStart, bool intoSecond, int inset)
    {
        if (secondStart - firstEnd > TouchingGap)
        {
            return (firstEnd + secondStart) / 2;
        }

        return intoSecond ? secondStart + inset : firstEnd - inset;
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

    /// <summary>
    /// The captions a resize drag must not start on: every pane caption and document tab in view, the resized pane's
    /// own included, since a drag that starts on a caption floats or re-docks that pane.
    /// </summary>
    public static List<T> Avoided<T>(IEnumerable<(string Title, T? Caption)> parts)
        where T : class => [.. parts.Select(p => p.Caption).OfType<T>()];

    /// <summary>Core's windows_mouse drag in window pixels, refused before the button goes down on an avoided ref.</summary>
    public static object DragArguments(string handle, Point from, Point to, string[] avoid) =>
        new
        {
            action = "drag",
            handle,
            from = new { x = from.X, y = from.Y },
            to = new { x = to.X, y = to.Y },
            fromMustNotHit = avoid,
        };
}
