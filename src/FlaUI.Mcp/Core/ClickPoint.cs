using System.Drawing;

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
);

public static class ClickPoint
{
    /// <summary>The point to press for an element, or the requested point on it.</summary>
    public static Point Choose(ClickGeometry geometry, Point? requested = null)
    {
        var bounds = geometry.Bounds;
        return requested ?? geometry.Clickable ?? new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
    }
}
