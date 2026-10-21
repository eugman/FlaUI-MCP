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
/// through MSAA, a Chromium node, a WPF text block), and no sibling of the target has bounds over the point.</item>
/// </list>
/// Anything else (a sibling, a scrollbar or overlay, the window, a far container, a failed hit test) is refused
/// with nothing sent. The returned kind lets the reply show how strong the proof was.
/// </summary>
public static class ClickHitTest
{
    public const int MaxAncestorLevels = 3;
    private const int MaxWalk = 50;

    public static string Verify(Point point, AutomationElement target) => Note(Prove(point, target));

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

        var covering = hit == null ? "nothing UI Automation can see" : desktop.DescribeElement(hit);
        if (hit == null || !IsNearContainer(desktop, target, hit))
        {
            throw Covered($"The point ({point.X},{point.Y}) is covered by {covering}");
        }

        // The container answered for the point, so check its children: a sibling over the point (z-order is not
        // available through UI Automation, so any sibling whose bounds hold it) would take the press.
        if (SiblingOver(desktop, target, point) is { } sibling)
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

    private static AutomationElement? SiblingOver(InputDesktop desktop, AutomationElement target, Point point)
    {
        if (desktop.ParentOf(target) is not { } parent)
        {
            return null;
        }

        return desktop
            .ChildrenOf(parent)
            .FirstOrDefault(child => !desktop.SameElement(child, target) && desktop.BoundsOf(child).Contains(point));
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
