using System.Drawing;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace FlaUI.Mcp.Core;

/// <summary>
/// The hit test a pointer action runs just before input, through the desktop seam. It passes when UI Automation
/// reports, at the press point:
/// <list type="bullet">
/// <item>the target itself ("target") or one of its descendants ("descendant"); or</item>
/// <item>an ancestor at most <see cref="MaxAncestorLevels"/> levels up that is not the top-level window
/// ("ancestor"), as providers that report the container for a point on its child do (a WinForms PropertyGrid row
/// through MSAA, a Chromium node, a WPF text block), and no onscreen sibling of the target or of an ancestor below
/// the hit has bounds over the point.</item>
/// </list>
/// Anything else (a sibling, a scrollbar or overlay, the window, a far container, a failed hit test) is refused
/// with nothing sent. The returned kind lets the reply show how strong the proof was. The limit of 3 levels is a
/// working value, pending the live check on TE3 PropertyGrid rows, PBI page tabs and the Camtasia timeline clip.
/// </summary>
public static class ClickHitTest
{
    public const int MaxAncestorLevels = 3;
    private const int MaxWalk = 50;

    public static string Verify(Point point, AutomationElement target) => Note(Prove(point, target));

    /// <summary>
    /// A host's own check on a pass-through press: why the press at <c>point</c> on <c>target</c> through
    /// <c>overlay</c> must be refused (Camtasia: an acting part such as a trim handle lies under the point), or null.
    /// </summary>
    public delegate string? PassThroughCheck(Point point, AutomationElement target, AutomationElement overlay);

    private sealed record PassThroughList(string[] AutomationIds, PassThroughCheck? Check);

    private static readonly PassThroughList NoPassThrough = new([], null);

    // Per flow, like InputDesktop's override: a registration applies to the flow that made it and the tasks it starts
    // afterwards. A host registers at startup, before it serves, so every call sees it; tests running side by side
    // no longer see each other's lists.
    private static readonly AsyncLocal<PassThroughList?> passThrough = new();

    private static PassThroughList Current => passThrough.Value ?? NoPassThrough;

    /// <summary>
    /// Opt-in for a host: overlays (by AutomationId) that do not take a press, such as Camtasia's selectionPlayhead
    /// line over a clip. A hit on one passes ("pass-through") when the point lies in the target's bounds and the
    /// overlay sits in the target's own top-level window, and they are not counted as covering siblings. Dispose to
    /// restore the previous list.
    /// </summary>
    public static IDisposable PassThrough(params string[] automationIds) => Register(new(automationIds, null));

    /// <inheritdoc cref="PassThrough(string[])"/>
    /// <param name="check">Run for every pass-through; a reason it returns refuses the press with nothing sent.</param>
    /// <param name="automationIds">The overlays that do not take a press.</param>
    public static IDisposable PassThrough(PassThroughCheck check, params string[] automationIds) =>
        Register(new(automationIds, check));

    private static Restore Register(PassThroughList list)
    {
        var previous = passThrough.Value;
        passThrough.Value = list;
        return new(previous);
    }

    private sealed class Restore(PassThroughList? previous) : IDisposable
    {
        public void Dispose() => passThrough.Value = previous;
    }

    private static bool Listed(InputDesktop desktop, AutomationElement element)
    {
        var listed = Current.AutomationIds;
        return listed.Length > 0 && listed.Contains(desktop.AutomationIdOf(element), StringComparer.Ordinal);
    }

    /// <summary>
    /// Why a listed overlay must not vouch for this press, or null when it passes: it has to sit in the target's own
    /// top-level window (an AutomationId alone matches any window's element of that name), and the host's check
    /// must not object.
    /// </summary>
    private static string? PassThroughRefusal(
        InputDesktop desktop,
        AutomationElement overlay,
        AutomationElement target,
        Point point
    )
    {
        var window = TopWindow(desktop, target);
        if (
            window == null
            || TopWindow(desktop, overlay) is not { } overlayWindow
            || !desktop.SameElement(window, overlayWindow)
        )
        {
            return "it is not in the target's window";
        }

        return Current.Check?.Invoke(point, target, overlay);
    }

    private static bool PassesThrough(
        InputDesktop desktop,
        AutomationElement element,
        AutomationElement target,
        Point point
    ) => Listed(desktop, element) && PassThroughRefusal(desktop, element, target, point) == null;

    /// <summary>The outermost Window above (or at) <paramref name="element"/>, or null when none can be read.</summary>
    private static AutomationElement? TopWindow(InputDesktop desktop, AutomationElement element)
    {
        AutomationElement? window = null;
        try
        {
            var current = (AutomationElement?)element;
            for (var level = 0; current != null && level <= MaxWalk; level++)
            {
                if (desktop.ControlTypeOf(current) == ControlType.Window)
                {
                    window = current;
                }

                current = desktop.ParentOf(current);
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return null;
        }

        return window;
    }

    /// <summary>
    /// Opt-in for a drag start on a raw point (TE3's splitter gaps): refuse before the button goes down when UI
    /// Automation reports one of <paramref name="mustNotHit"/> (or something inside it) at the point.
    /// </summary>
    public static void RequireNotWithin(
        Point point,
        AutomationElement target,
        IEnumerable<AutomationElement> mustNotHit
    )
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

        if (
            hit != null
            && mustNotHit.Any(element =>
                desktop.SameElement(hit, element) || Levels(desktop, hit, element, MaxWalk) > 0
            )
        )
        {
            throw Covered(
                $"The drag start ({point.X},{point.Y}) is on {desktop.DescribeElement(hit)}, which must not be pressed"
            );
        }
    }

    /// <summary>Keep the weakest proof of the running operation, for its reply's hitTest field.</summary>
    private static string Note(string kind)
    {
        if (OperationContext.Current.Value is { } operation && Rank(kind) > Rank(operation.HitTest))
        {
            operation.HitTest = kind;
        }

        return kind;
    }

    private static int Rank(string? kind) =>
        kind switch
        {
            "target" => 1,
            "descendant" => 2,
            "ancestor" => 3,
            "pass-through" => 4,
            _ => 0,
        };

    private static string Prove(Point point, AutomationElement target)
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

        if (hit != null && desktop.SameElement(hit, target))
        {
            return "target";
        }

        if (Levels(desktop, hit, target, MaxWalk) > 0)
        {
            return "descendant";
        }

        if (hit != null && Listed(desktop, hit) && desktop.BoundsOf(target).Contains(point))
        {
            if (PassThroughRefusal(desktop, hit, target, point) is { } refused)
            {
                throw Covered(
                    $"The point ({point.X},{point.Y}) is on {desktop.DescribeElement(hit)}, which passes the press through, but {refused}"
                );
            }

            return "pass-through";
        }

        var covering = hit == null ? "nothing UI Automation can see" : desktop.DescribeElement(hit);
        if (hit == null || !IsNearContainer(desktop, target, hit))
        {
            throw Covered($"The point ({point.X},{point.Y}) is covered by {covering}");
        }

        // The container answered for the point, so check what lies beside the path down to the target: a sibling of
        // the target, or of any ancestor below the hit ("uncles"), over the point would take the press. UI
        // Automation gives no z-order, so any onscreen one whose bounds hold the point refuses.
        if (OverlayOver(desktop, target, hit, point) is { } sibling)
        {
            var unread = sibling.Bounds == null ? ", whose bounds could not be read" : "";
            throw Covered(
                $"The point ({point.X},{point.Y}) is covered by {desktop.DescribeElement(sibling.Element)}{unread}"
            );
        }

        return "ancestor";
    }

    /// <summary>A container of the target, close to it, that is not the top-level window.</summary>
    private static bool IsNearContainer(InputDesktop desktop, AutomationElement target, AutomationElement hit)
    {
        var levels = Levels(desktop, target, hit, MaxAncestorLevels);
        return levels > 0 && desktop.ControlTypeOf(hit) != ControlType.Window;
    }

    // Skipped only when IsOffscreen was read as true; a sibling whose bounds cannot be read counts as covering.
    private static bool MayCover(ChildBox box, Point point) =>
        box.IsOffscreen != true && (box.Bounds is not { } bounds || bounds.Contains(point));

    /// <summary>The children beside the path; a failed read refuses the press, as an unreadable hit does.</summary>
    private static IReadOnlyList<ChildBox> Siblings(InputDesktop desktop, AutomationElement parent, Point point)
    {
        try
        {
            return desktop.ChildBoxes(parent);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            throw Covered(
                $"The point ({point.X},{point.Y}) could not be checked for overlays: the elements beside the target "
                    + $"could not be read ({error.Message})"
            );
        }
    }

    private static ChildBox? OverlayOver(
        InputDesktop desktop,
        AutomationElement target,
        AutomationElement hit,
        Point point
    )
    {
        var child = target;
        for (var level = 0; level < MaxAncestorLevels && desktop.ParentOf(child) is { } parent; level++)
        {
            var overlay = Siblings(desktop, parent, point)
                .FirstOrDefault(box =>
                    MayCover(box, point)
                    && !desktop.SameElement(box.Element, child)
                    && !PassesThrough(desktop, box.Element, target, point)
                );
            if (overlay != null)
            {
                return overlay;
            }

            if (desktop.SameElement(parent, hit))
            {
                return null;
            }

            child = parent;
        }

        return null;
    }

    /// <summary>How many parent steps lead from <paramref name="element"/> up to <paramref name="ancestor"/>
    /// (at most <paramref name="limit"/>), or 0 when it is not reached.</summary>
    private static int Levels(InputDesktop desktop, AutomationElement? element, AutomationElement ancestor, int limit)
    {
        var current = element == null ? null : desktop.ParentOf(element);
        for (var level = 1; current != null && level <= limit; level++)
        {
            if (desktop.SameElement(current, ancestor))
            {
                return level;
            }

            current = desktop.ParentOf(current);
        }

        return 0;
    }

    private static InputRefusedException Covered(string reason) =>
        InputRefusedException.Record(
            new InputRefusal(
                "click-point-covered",
                $"{reason}; no input sent. Scroll the element into view, then observe again."
            )
        );
}
