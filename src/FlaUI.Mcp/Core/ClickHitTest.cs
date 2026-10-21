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

    private static string[] passThrough = [];

    /// <summary>
    /// Opt-in for a host: overlays (by AutomationId) that do not take a press, such as Camtasia's selectionPlayhead
    /// line over a clip. A hit on one passes ("pass-through") when the point lies in the target's bounds, and they
    /// are not counted as covering siblings. Dispose to restore the previous list.
    /// </summary>
    public static IDisposable PassThrough(params string[] automationIds)
    {
        var previous = Interlocked.Exchange(ref passThrough, automationIds);
        return new Restore(previous);
    }

    private sealed class Restore(string[] previous) : IDisposable
    {
        public void Dispose() => Volatile.Write(ref passThrough, previous);
    }

    private static bool PassesThrough(InputDesktop desktop, AutomationElement element)
    {
        var listed = Volatile.Read(ref passThrough);
        return listed.Length > 0 && listed.Contains(desktop.AutomationIdOf(element), StringComparer.Ordinal);
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

        if (hit != null && PassesThrough(desktop, hit) && desktop.BoundsOf(target).Contains(point))
        {
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
            throw Covered($"The point ({point.X},{point.Y}) is covered by {desktop.DescribeElement(sibling)}");
        }

        return "ancestor";
    }

    /// <summary>A container of the target, close to it, that is not the top-level window.</summary>
    private static bool IsNearContainer(InputDesktop desktop, AutomationElement target, AutomationElement hit)
    {
        var levels = Levels(desktop, target, hit, MaxAncestorLevels);
        return levels > 0 && desktop.ControlTypeOf(hit) != ControlType.Window;
    }

    private static AutomationElement? OverlayOver(
        InputDesktop desktop,
        AutomationElement target,
        AutomationElement hit,
        Point point
    )
    {
        var child = target;
        for (var level = 0; level < MaxAncestorLevels && desktop.ParentOf(child) is { } parent; level++)
        {
            var overlay = desktop
                .ChildBoxes(parent)
                .FirstOrDefault(box =>
                    !box.IsOffscreen
                    && box.Bounds.Contains(point)
                    && !desktop.SameElement(box.Element, child)
                    && !PassesThrough(desktop, box.Element)
                );
            if (overlay != null)
            {
                return overlay.Element;
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
