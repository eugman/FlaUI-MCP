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
        using var lease = GuardedInput.AcquireLease();
        var before = bounds();
        var result = ModalAwareInvoker.Execute(
            processId,
            "ScrollIntoView",
            () =>
            {
                OperationContext.Check();
                validate();
                OperationContext.Check();
                MutationGuard.CheckPermission();
                InputDesktop.Current.CallPattern(scroll);
            },
            pending
        );
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
