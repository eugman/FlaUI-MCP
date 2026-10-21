using System.Drawing;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace FlaUI.Mcp.Core;

/// <summary>What a pointer action needs to know about an element, read once so the choice is testable.</summary>
/// <param name="IsOffscreen">UI Automation's IsOffscreen.</param>
/// <param name="Clickable">The provider's clickable point, or null when it has none.</param>
/// <param name="Bounds">The element's bounding rectangle in screen pixels.</param>
/// <param name="ScrollableAncestors">Bounds of every ancestor that scrolls, up to its window.</param>
public sealed record ClickGeometry(
    bool IsOffscreen,
    Point? Clickable,
    Rectangle Bounds,
    IReadOnlyList<Rectangle> ScrollableAncestors
)
{
    private const int MaxAncestors = 50;

    /// <summary>Scroll the element into view when it supports that, then read its geometry.</summary>
    public static ClickGeometry Read(AutomationElement element)
    {
        OperationContext.Check();
        if (element.Patterns.ScrollItem.TryGetPattern(out var scrollItem))
        {
            scrollItem.ScrollIntoView();
        }

        var scrollers = new List<Rectangle>();
        var ancestor = element.Parent;
        for (var depth = 0; ancestor != null && depth < MaxAncestors; depth++)
        {
            if (ancestor.Patterns.Scroll.IsSupported)
            {
                scrollers.Add(ancestor.BoundingRectangle);
            }

            if (ancestor.Properties.ControlType.ValueOrDefault == ControlType.Window)
            {
                break;
            }

            ancestor = ancestor.Parent;
        }

        Point? clickable = element.TryGetClickablePoint(out var point) ? point : null;
        return new(element.Properties.IsOffscreen.ValueOrDefault, clickable, element.BoundingRectangle, scrollers);
    }
}

/// <summary>Where a pointer action may press on an element: only where the element is visible.</summary>
public static class ClickPoint
{
    /// <summary>
    /// The element's clickable point, or <paramref name="requested"/> (such as an edge), checked to lie inside
    /// the element and every scrolling ancestor. Never guesses the centre of an element it cannot see.
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

        var point = requested ?? geometry.Clickable ?? throw Refuse("The element reports no clickable point");
        if (!geometry.Bounds.Contains(point))
        {
            throw Refuse($"The point ({point.X},{point.Y}) is outside the element's bounds");
        }

        if (geometry.ScrollableAncestors.Any(viewport => !viewport.Contains(point)))
        {
            throw Refuse($"The point ({point.X},{point.Y}) is scrolled out of view of a containing list or pane");
        }

        return point;
    }

    private static InputRefusedException Refuse(string reason) =>
        InputRefusedException.Record(
            new InputRefusal(
                "click-point-unavailable",
                $"{reason}; no input sent. Scroll it into view, then observe again."
            )
        );
}
