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
                    (int)(from.X + (long)Math.Round(((long)to.X - from.X) * (double)i / steps)),
                    (int)(from.Y + (long)Math.Round(((long)to.Y - from.Y) * (double)i / steps))
                )
            );
        }

        return path;
    }

    /// <summary>
    /// Where to park the pointer for <paramref name="window"/>, always outside it: just beside it on the monitor
    /// that holds most of it, so the pointer never jumps to another screen; else just beside it on a neighbouring
    /// screen; else a free corner of its monitor. Null when the window is on no monitor or covers every point
    /// these offer (a full-screen window on a single screen).
    /// </summary>
    public static Point? ParkPoint(Rectangle window, IReadOnlyList<Rectangle> monitors)
    {
        const int Inset = 8;
        var holder = monitors
            .Where(m => m.IntersectsWith(window))
            .OrderByDescending(m => Area(Rectangle.Intersect(m, window)))
            .Select(m => (Rectangle?)m)
            .FirstOrDefault();
        if (holder is not { } monitor)
        {
            return null;
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
        Point[] corners =
        [
            new(monitor.X + Inset, monitor.Bottom - 1 - Inset),
            new(monitor.Right - 1 - Inset, monitor.Bottom - 1 - Inset),
            new(monitor.X + Inset, monitor.Y + Inset),
            new(monitor.Right - 1 - Inset, monitor.Y + Inset),
        ];
        return candidates
            .Where(monitor.Contains)
            .Concat(candidates.Where(p => monitors.Any(m => m.Contains(p))))
            .Concat(corners)
            .Where(p => !window.Contains(p))
            .Select(p => (Point?)p)
            .FirstOrDefault();
    }

    private static long Area(Rectangle r) => (long)r.Width * r.Height;

    public static readonly string[] Edges = ["center", "left", "right", "top", "bottom"];

    /// <summary>
    /// How far inside its edge a drop point lands: the outermost pixel of a row or pane is routinely its
    /// neighbour's border, which takes the drop instead.
    /// </summary>
    public const int DropInset = 2;

    /// <summary>
    /// The middle of one edge of <paramref name="r"/>, or its centre. With no inset the point is the edge's own
    /// first or last pixel; <paramref name="inset"/> moves it that many pixels inward, symmetric on every side
    /// and clamped so it stays inside a rectangle thinner than two insets.
    /// </summary>
    public static Point EdgePoint(Rectangle r, string edge, int inset = 0)
    {
        var midX = r.X + r.Width / 2;
        var midY = r.Y + r.Height / 2;
        var dx = Math.Min(inset, Math.Max(0, (r.Width - 1) / 2));
        var dy = Math.Min(inset, Math.Max(0, (r.Height - 1) / 2));
        return edge switch
        {
            "left" => new Point(r.Left + dx, midY),
            "right" => new Point(r.Right - 1 - dx, midY),
            "top" => new Point(midX, r.Top + dy),
            "bottom" => new Point(midX, r.Bottom - 1 - dy),
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

    /// <summary>
    /// Move the pointer to <see cref="PointerPlan.ParkPoint"/>, under the lease and the permission check, after the
    /// integrity check of the window under that point. Refuses with no input when no point is off the window.
    /// </summary>
    public static Point Park(Rectangle window)
    {
        var desktop = InputDesktop.Current;
        var monitors = desktop.Monitors();
        var point = PointerPlan.ParkPoint(window, monitors) ?? throw NoParkPoint(window, monitors);
        using (GuardedInput.AcquireLease())
        {
            MutationGuard.Execute(
                () =>
                {
                    OperationContext.Check();
                    // Windows drops input to a higher-integrity window, the pointer's among it.
                    var under = desktop.WindowAt(point);
                    if (under != 0 && desktop.ProcessOf(under) is var pid and not 0)
                    {
                        GuardedInput.RequireDrivable(pid, desktop);
                    }
                },
                () => desktop.MovePointer(point)
            );
        }

        Thread.Sleep(Settle);
        return desktop.PointerPosition();
    }

    private static InvalidOperationException NoParkPoint(Rectangle window, IReadOnlyList<Rectangle> monitors) =>
        new(
            monitors.Any(m => m.IntersectsWith(window))
                ? "The window covers its screen, so the pointer could not leave the window; no input sent. Restore or move the window, or capture without parkPointer."
                : "The window is on no monitor, so there is no screen to park the pointer on; no input sent. Move the window onto a screen first."
        );
}
