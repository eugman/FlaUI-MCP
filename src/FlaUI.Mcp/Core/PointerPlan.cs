using System.Drawing;

namespace FlaUI.Mcp.Core;

/// <summary>Pure pointer geometry: drag paths and where to park the pointer out of a capture.</summary>
public static class PointerPlan
{
    public const int DefaultDragSteps = 12;
    public const int MaxDragSteps = 200;

    /// <summary>Intermediate points from <paramref name="from"/> to <paramref name="to"/>, ending exactly on it.</summary>
    public static IReadOnlyList<Point> DragPath(Point from, Point to, int steps)
    {
        if (steps is < 1 or > MaxDragSteps)
        {
            throw new ArgumentException($"steps must be 1..{MaxDragSteps}");
        }

        var path = new List<Point>(steps);
        for (var i = 1; i <= steps; i++)
        {
            path.Add(
                new Point(
                    from.X + (int)Math.Round((to.X - from.X) * (double)i / steps),
                    from.Y + (int)Math.Round((to.Y - from.Y) * (double)i / steps)
                )
            );
        }

        return path;
    }

    /// <summary>
    /// A screen point outside <paramref name="window"/>, so no control of it shows hover state or a
    /// tooltip. Prefers another monitor, then the margins of the window's own monitor; null when the
    /// window covers every screen pixel.
    /// </summary>
    public static Point? ParkPoint(Rectangle window, IReadOnlyList<Rectangle> monitors)
    {
        const int Inset = 8;
        if (monitors.FirstOrDefault(m => !m.IntersectsWith(window)) is { IsEmpty: false } elsewhere)
        {
            return new Point(elsewhere.X + Inset, elsewhere.Y + Inset);
        }

        // A quarter of the way along a bottom taskbar: clear of a centered Windows 11 icon
        // group (thumbnail popups), the widgets button (far left) and the clock (far right).
        Point[] candidates =
        [
            new(window.Right + Inset, window.Y + window.Height / 2),
            new(window.X + window.Width / 4, window.Bottom + Inset),
            new(window.X - Inset, window.Y + window.Height / 2),
            new(window.X + window.Width / 2, window.Y - Inset),
        ];
        var own = monitors.Where(m => m.IntersectsWith(window)).ToList();
        return candidates.Where(p => own.Any(m => m.Contains(p))).Select(p => (Point?)p).FirstOrDefault();
    }

    public static readonly string[] Edges = ["center", "left", "right", "top", "bottom"];

    /// <summary>The middle of one edge of <paramref name="r"/>, one pixel inside it, or its centre.</summary>
    public static Point EdgePoint(Rectangle r, string edge)
    {
        var midX = r.X + r.Width / 2;
        var midY = r.Y + r.Height / 2;
        return edge switch
        {
            "left" => new Point(r.Left, midY),
            "right" => new Point(r.Right - 1, midY),
            "top" => new Point(midX, r.Top),
            "bottom" => new Point(midX, r.Bottom - 1),
            "center" => new Point(midX, midY),
            _ => throw new ArgumentException("edge must be center, left, right, top or bottom"),
        };
    }

    /// <summary>
    /// Where to release an edge drag so <paramref name="r"/> ends up <paramref name="size"/> pixels
    /// wide (left/right edge) or tall (top/bottom edge), with the opposite edge fixed.
    /// </summary>
    public static Point ResizeTarget(Rectangle r, string edge, int size)
    {
        if (size < 1)
        {
            throw new ArgumentException("resizeTo must be at least 1 pixel");
        }

        var start = EdgePoint(r, edge);
        return edge switch
        {
            "right" => start with { X = r.Left + size - 1 },
            "left" => start with { X = r.Right - size },
            "bottom" => start with { Y = r.Top + size - 1 },
            "top" => start with { Y = r.Bottom - size },
            _ => throw new ArgumentException("resizeTo needs fromEdge left, right, top or bottom"),
        };
    }

    /// <summary>Window-relative image pixels to screen pixels; the origin is what windows_screenshot captures.</summary>
    public static Point ToScreen(Rectangle origin, Point relative) => new(origin.X + relative.X, origin.Y + relative.Y);
}

/// <summary>Moves the pointer off a window so no hover state or tooltip of it reaches a capture.</summary>
public static class PointerParking
{
    // Long enough for a tooltip to fade and a hot-tracked control to repaint.
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(250);

    public static Point Park(Rectangle window)
    {
        var monitors = System.Windows.Forms.Screen.AllScreens.Select(s => s.Bounds).ToList();
        var point =
            PointerPlan.ParkPoint(window, monitors)
            ?? throw new InvalidOperationException(
                "The window covers every screen, so the pointer cannot leave it; restore the window or move the pointer to a quiet spot with windows_mouse."
            );
        using (GuardedInput.AcquireLease())
        {
            OperationContext.Check();
            FlaUI.Core.Input.Mouse.MoveTo(point);
        }

        Thread.Sleep(Settle);
        return FlaUI.Core.Input.Mouse.Position;
    }
}
