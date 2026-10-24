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
/// <param name="VerticallyScrollable">
/// Those viewports whose content extends beyond them vertically now (the Scroll pattern's VerticallyScrollable with
/// a view smaller than the content): the ones a resting drag auto-scrolls. Null when unread.
/// </param>
public sealed record ClickGeometry(
    bool IsOffscreen,
    Point? Clickable,
    Rectangle Bounds,
    IReadOnlyList<Rectangle> ScrollableAncestors,
    IReadOnlyList<Rectangle>? VerticallyScrollable = null
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
        Action validate,
        InputTarget? target = null
    ) =>
        element.Patterns.ScrollItem.TryGetPattern(out var scrollItem)
            ? Scroll(processId, pending, validate, scrollItem.ScrollIntoView, () => element.BoundingRectangle, target)
            : null;

    internal static string Scroll(
        int processId,
        PendingInvokeTracker pending,
        Action validate,
        Action scroll,
        Func<Rectangle> bounds,
        InputTarget? target = null
    )
    {
        OperationContext.Check();
        // Activate before scrolling: if Windows keeps another app in front, leave the target untouched.
        // Release this lease before WindowMutation.Uia acquires its own (the desktop lease is not reentrant).
        string? activated = null;
        if (target != null)
        {
            using var preflight = new GuardedInput(target);
            activated = preflight.Changed;
        }

        var before = bounds();
        PatternCallResult result;
        try
        {
            // The guarded pattern call (lease, validation, integrity, permission), counted below only when it moved.
            result = WindowMutation.Uia(
                processId,
                "ScrollIntoView",
                scroll,
                pending,
                validate,
                options: new(false, target)
            );
        }
        catch (Exception error) when (activated != null && GuardedInput.IsRefusal(error))
        {
            throw GuardedInput.ChangedFirst(error, activated);
        }

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
    // An element that is the bar or lies in its subtree (a column header, a scrollbar thumb) keeps it.
    private static Rectangle Viewport(AutomationElement scroller, AutomationElement element, Rectangle bounds) =>
        Viewport(
            scroller.BoundingRectangle,
            scroller
                .FindAllChildren(cf => cf.ByControlType(ControlType.ScrollBar).Or(cf.ByControlType(ControlType.Header)))
                .Select(chrome => (chrome.BoundingRectangle, InSubtree(element, chrome, scroller))),
            bounds
        );

    /// <summary>Whether <paramref name="element"/> is <paramref name="bar"/> or under it, below <paramref name="scroller"/>.</summary>
    private static bool InSubtree(AutomationElement element, AutomationElement bar, AutomationElement scroller)
    {
        var desktop = InputDesktop.Current;
        AutomationElement? current = element;
        for (var depth = 0; current != null && depth < MaxAncestors; depth++)
        {
            if (desktop.SameElement(current, bar))
            {
                return true;
            }

            if (desktop.SameElement(current, scroller))
            {
                return false;
            }

            current = desktop.ParentOf(current);
        }

        return false;
    }

    internal static Rectangle Viewport(Rectangle bounds, IEnumerable<Rectangle> chrome, Rectangle? element = null) =>
        Viewport(bounds, chrome.Select(bar => (bar, false)), element);

    /// <summary>
    /// <paramref name="bounds"/> without the scrollbars and headers in <paramref name="chrome"/>: each is cut off the
    /// edge it lies along (a wide one off the top or bottom, a tall one off the left or right). Empty when nothing
    /// is left. The cut is for content: a bar is kept when <paramref name="element"/> is that bar or in its subtree
    /// (HoldsElement, a column header or a resize handle) or lies fully inside its bounds. A row merely half under a
    /// scrollbar still loses the bar.
    /// </summary>
    internal static Rectangle Viewport(
        Rectangle bounds,
        IEnumerable<(Rectangle Bounds, bool HoldsElement)> chrome,
        Rectangle? element = null
    )
    {
        var (left, top, right, bottom) = (bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
        var cut = chrome
            .Where(bar => !bar.HoldsElement && !(element is { } e && bar.Bounds.Contains(e)))
            .Select(bar => bar.Bounds);
        foreach (var bar in cut.Where(bar => bar.IntersectsWith(bounds)))
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
        var bounds = element.BoundingRectangle;
        var scrollers = new List<Rectangle>();
        var vertical = new List<Rectangle>();
        var ancestor = element.Parent;
        for (var depth = 0; ancestor != null && depth < MaxAncestors; depth++)
        {
            if (ancestor.Patterns.Scroll.TryGetPattern(out var scroll))
            {
                var viewport = Viewport(ancestor, element, bounds);
                scrollers.Add(viewport);
                if (scroll.VerticallyScrollable.ValueOrDefault && scroll.VerticalViewSize.ValueOrDefault < 100)
                {
                    vertical.Add(viewport);
                }
            }

            if (ancestor.Properties.ControlType.ValueOrDefault == ControlType.Window)
            {
                break;
            }

            ancestor = ancestor.Parent;
        }

        var clickable = UsableClickable(element.TryGetClickablePoint(out var point), point, bounds);
        return new(element.Properties.IsOffscreen.ValueOrDefault, clickable, bounds, scrollers, vertical);
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

    /// <summary>
    /// Refuse a drop point within one row (the element's own height) of the top or bottom of an ancestor's viewport
    /// whose content scrolls vertically now: a drag resting there auto-scrolls the view (a tree did, six rows), so
    /// the release lands on another row. A list that holds all its rows, or a viewport shorter than three such
    /// bands, has none.
    /// </summary>
    public static void RequireOutsideScrollBand(Point point, ClickGeometry geometry)
    {
        var band = geometry.Bounds.Height;
        foreach (var viewport in (geometry.VerticallyScrollable ?? []).Where(v => v.Height >= 3 * band))
        {
            string? edge = null;
            if (point.Y < viewport.Top + band)
            {
                edge = "top";
            }
            else if (point.Y >= viewport.Bottom - band)
            {
                edge = "bottom";
            }

            if (edge != null)
            {
                throw InputRefusedException.Record(
                    new InputRefusal(
                        RefusalCodes.DropPointInScrollBand,
                        $"The drop point ({point.X},{point.Y}) is within {band} px of the {edge} of a scrolling list "
                            + $"or pane (its viewport is ({viewport.X},{viewport.Y} {viewport.Width}x{viewport.Height})); "
                            + "a drag resting there scrolls the view, so the release would land on another row; no "
                            + "input sent. Scroll the view so the target is away from that edge, then observe and retry."
                    )
                );
            }
        }
    }

    private static Rectangle VisiblePart(ClickGeometry geometry) =>
        geometry.ScrollableAncestors.Aggregate(geometry.Bounds, Rectangle.Intersect);

    /// <summary>
    /// Hit-test <paramref name="point"/> just before input; see <see cref="ClickHitTest.Verify"/>. Returns how the
    /// point was proven to be on the element: "target", "descendant" or "ancestor".
    /// </summary>
    public static string Verify(Point point, AutomationElement target) => ClickHitTest.Verify(point, target);

    /// <summary>
    /// Why <paramref name="point"/>, chosen on <paramref name="target"/> earlier, is no longer on it, or null: the
    /// bounds read <paramref name="now"/> must still hold the point and the hit test must still pass. For a check
    /// after input, so the reason never claims nothing was sent.
    /// </summary>
    public static string? Moved(Point point, ClickGeometry now, AutomationElement target)
    {
        var bounds = now.Bounds;
        if (!bounds.Contains(point))
        {
            return $"{InputDesktop.Current.DescribeElement(target)} is now at ({bounds.X},{bounds.Y} "
                + $"{bounds.Width}x{bounds.Height}), which no longer holds the drop point ({point.X},{point.Y})";
        }

        return ClickHitTest.Covering(point, target);
    }

    private static InputRefusedException Refuse(string reason) =>
        InputRefusedException.Record(
            new InputRefusal(
                "click-point-unavailable",
                $"{reason}; no input sent. Scroll it into view, then observe again."
            )
        );
}
