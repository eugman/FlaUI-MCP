using System.Drawing;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace FlaUI.Mcp.Core;

/// <summary>What a pointer action needs to know about an element, read once so the choice is testable.</summary>
/// <param name="IsOffscreen">UI Automation's IsOffscreen.</param>
/// <param name="Clickable">The provider's clickable point, or null when it has none.</param>
/// <param name="Bounds">The element's bounding rectangle in screen pixels.</param>
/// <param name="ScrollableAncestors">
/// The viewport of every ancestor that scrolls, up to its window: its bounds without its own scrollbars and headers.
/// </param>
public sealed record ClickGeometry(
    bool IsOffscreen,
    Point? Clickable,
    Rectangle Bounds,
    IReadOnlyList<Rectangle> ScrollableAncestors
)
{
    private const int MaxAncestors = 50;

    /// <summary>
    /// Scroll the element into view when it supports that. Scrolling changes the app as input does, so it takes
    /// the input lease, revalidates the target and runs modal-aware; it counts as a dispatch only when the
    /// element moved. Call it before guarded input takes the lease. Returns the note a later refusal carries
    /// ("the view was scrolled" or "a scroll into view was requested"), or null when the element cannot scroll.
    /// </summary>
    public static string? ScrollIntoView(
        AutomationElement element,
        int processId,
        PendingInvokeTracker pending,
        Action validate
    ) =>
        element.Patterns.ScrollItem.TryGetPattern(out var scrollItem)
            ? Scroll(processId, pending, validate, scrollItem.ScrollIntoView, () => element.BoundingRectangle)
            : null;

    internal static string Scroll(
        int processId,
        PendingInvokeTracker pending,
        Action validate,
        Action scroll,
        Func<Rectangle> bounds
    )
    {
        OperationContext.Check();
        var before = bounds();
        // The guarded pattern call (lease, validation, integrity, permission), counted below only when it moved.
        var result = WindowMutation.Uia(processId, "ScrollIntoView", scroll, pending, validate, counted: false);
        if (result.Outcome != PatternCallOutcome.Completed)
        {
            // The tracker still holds the call, so the registry marks the outcome's provider pending.
            throw new InvalidOperationException(
                "ScrollIntoView did not return; the app's UI Automation provider may be blocked. Nothing was "
                    + "pressed. Check windows_operation_status before retrying."
            );
        }

        if (bounds() == before)
        {
            return "a scroll into view was requested";
        }

        MutationGuard.RecordDispatch();
        return "the view was scrolled";
    }

    /// <summary>
    /// The provider's clickable point when it is a real one. UI Automation can report success with the
    /// (int.MinValue, int.MinValue) "no point" sentinel (a WPF slider does), and a point outside the element's
    /// own bounds belongs to something else; both count as no clickable point.
    /// </summary>
    internal static Point? UsableClickable(bool reported, Point point, Rectangle bounds) =>
        reported && !IsSentinel(point) && bounds.Contains(point) ? point : null;

    private static bool IsSentinel(Point point) => point.X == int.MinValue || point.Y == int.MinValue;

    // A scroller's bounds include its own scrollbars and column headers, which take a press meant for content.
    private static Rectangle Viewport(AutomationElement scroller) =>
        Viewport(
            scroller.BoundingRectangle,
            scroller
                .FindAllChildren(cf => cf.ByControlType(ControlType.ScrollBar).Or(cf.ByControlType(ControlType.Header)))
                .Select(chrome => chrome.BoundingRectangle)
        );

    /// <summary>
    /// <paramref name="bounds"/> without the scrollbars and headers in <paramref name="chrome"/>: each is cut off the
    /// edge it lies along (a wide one off the top or bottom, a tall one off the left or right). Empty when nothing
    /// is left.
    /// </summary>
    internal static Rectangle Viewport(Rectangle bounds, IEnumerable<Rectangle> chrome)
    {
        var (left, top, right, bottom) = (bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
        foreach (var bar in chrome.Where(bar => bar.IntersectsWith(bounds)))
        {
            if (bar.Width >= bar.Height)
            {
                (top, bottom) = CutAlong(top, bottom, bar.Top, bar.Bottom);
            }
            else
            {
                (left, right) = CutAlong(left, right, bar.Left, bar.Right);
            }
        }

        return right > left && bottom > top ? Rectangle.FromLTRB(left, top, right, bottom) : Rectangle.Empty;
    }

    /// <summary>Cut a bar off the near end of a span, or off its far end when the bar's middle is past the span's.</summary>
    private static (int Start, int End) CutAlong(int start, int end, int barStart, int barEnd) =>
        barStart + barEnd > start + end ? (start, Math.Min(end, barStart)) : (Math.Max(start, barEnd), end);

    /// <summary>Read the element's geometry. It does not scroll; see <see cref="ScrollIntoView"/>.</summary>
    public static ClickGeometry Read(AutomationElement element)
    {
        OperationContext.Check();
        var scrollers = new List<Rectangle>();
        var ancestor = element.Parent;
        for (var depth = 0; ancestor != null && depth < MaxAncestors; depth++)
        {
            if (ancestor.Patterns.Scroll.IsSupported)
            {
                scrollers.Add(Viewport(ancestor));
            }

            if (ancestor.Properties.ControlType.ValueOrDefault == ControlType.Window)
            {
                break;
            }

            ancestor = ancestor.Parent;
        }

        var bounds = element.BoundingRectangle;
        var clickable = UsableClickable(element.TryGetClickablePoint(out var point), point, bounds);
        return new(element.Properties.IsOffscreen.ValueOrDefault, clickable, bounds, scrollers);
    }
}

/// <summary>Where a pointer action may press on an element: only where the element is visible.</summary>
public static class ClickPoint
{
    /// <summary>
    /// The element's clickable point, or <paramref name="requested"/> (such as an edge), checked to lie inside
    /// the element and every scrolling ancestor. Without a clickable point it uses the centre of the bounds,
    /// but only for an element that is not offscreen and has bounds: the centre of an empty rectangle is (0,0),
    /// a maximized window's caption. The viewport check below still applies to the centre.
    /// </summary>
    public static Point Choose(ClickGeometry geometry, Point? requested = null)
    {
        if (geometry.IsOffscreen)
        {
            throw Refuse("The element is offscreen (scrolled away or hidden)");
        }

        if (geometry.Bounds.IsEmpty)
        {
            throw Refuse("The element has no on-screen bounds");
        }

        var bounds = geometry.Bounds;
        var point =
            requested ?? geometry.Clickable ?? new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
        if (!geometry.Bounds.Contains(point))
        {
            throw Refuse($"The point ({point.X},{point.Y}) is outside the element's bounds");
        }

        if (geometry.ScrollableAncestors.All(viewport => viewport.Contains(point)))
        {
            return point;
        }

        // The element's own point is hidden (under a scrollbar, say): press the middle of what is visible of it.
        // A requested point, such as an edge, means exactly that point, so it is never moved.
        if (requested == null && VisiblePart(geometry) is { Width: >= MinVisible, Height: >= MinVisible } visible)
        {
            return new Point(visible.X + visible.Width / 2, visible.Y + visible.Height / 2);
        }

        throw Refuse($"The point ({point.X},{point.Y}) is scrolled out of view of a containing list or pane");
    }

    private const int MinVisible = 4;

    private static Rectangle VisiblePart(ClickGeometry geometry) =>
        geometry.ScrollableAncestors.Aggregate(geometry.Bounds, Rectangle.Intersect);

    /// <summary>
    /// Hit-test <paramref name="point"/> just before input: UI Automation must report <paramref name="target"/> or
    /// one of its descendants there. A sibling, scrollbar, header or overlay in the same window is refused by name
    /// with nothing sent, and so is a hit test that fails.
    /// </summary>
    public static void Verify(Point point, AutomationElement target, ClickGeometry geometry)
    {
        var desktop = InputDesktop.Current;
        AutomationElement? hit;
        try
        {
            hit = desktop.ElementAt(target, point);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            throw Covered($"The point ({point.X},{point.Y}) could not be hit-tested ({error.Message})");
        }

        if (Within(desktop, hit, target))
        {
            return;
        }

        // Some providers report the container for a point on its child (a WinForms PropertyGrid row through
        // MSAA, a Chromium node, a WPF text block): an ancestor counts, but only where the target is visible.
        if (hit != null && VisiblePart(geometry).Contains(point) && Within(desktop, target, hit))
        {
            return;
        }

        var covering = hit == null ? "nothing UI Automation can see" : desktop.DescribeElement(hit);
        throw Covered($"The point ({point.X},{point.Y}) is covered by {covering}");
    }

    private const int MaxAncestorWalk = 50;

    /// <summary>Whether <paramref name="element"/> is <paramref name="ancestor"/> or lies under it.</summary>
    private static bool Within(InputDesktop desktop, AutomationElement? element, AutomationElement ancestor)
    {
        for (var depth = 0; element != null && depth < MaxAncestorWalk; depth++)
        {
            if (desktop.SameElement(element, ancestor))
            {
                return true;
            }

            element = desktop.ParentOf(element);
        }

        return false;
    }

    private static InputRefusedException Covered(string reason) =>
        InputRefusedException.Record(
            new InputRefusal(
                "click-point-covered",
                $"{reason}; no input sent. Scroll the element into view, then observe again."
            )
        );

    private static InputRefusedException Refuse(string reason) =>
        InputRefusedException.Record(
            new InputRefusal(
                "click-point-unavailable",
                $"{reason}; no input sent. Scroll it into view, then observe again."
            )
        );
}
