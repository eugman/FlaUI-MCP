using System.Drawing;
using FlaUI.Core.AutomationElements;
using FlaUI.Mcp.Core;

namespace FlaUI.Mcp.Tools;

/// <summary>
/// A pointer action's point: chosen and hit-tested under the lease (<see cref="Choose"/>), and checked again
/// before a drag's release (<see cref="Moved"/>: why it is no longer on its target, or null).
/// </summary>
internal sealed record Press(Func<Point> Choose, Func<Point, string?> Moved)
{
    /// <summary>A raw window point: nothing to re-read, so only the window under it is checked again.</summary>
    public static Press Fixed(Point point) => new(() => point, _ => null);
}

/// <summary>Points on refs for windows_mouse: scrolled into view through the guards, then read under the lease.</summary>
internal sealed class RefPoints(ElementRegistry refs, PendingInvokeTracker pending)
{
    public AutomationElement Resolve(string reference) => refs.ResolveRef(reference);

    /// <summary>
    /// Scroll a ref into view through the guards now; the element and a reader for its geometry, which runs
    /// under the input lease.
    /// </summary>
    public (AutomationElement Element, Func<ClickGeometry> Read) Located(string reference, ref string? scrolled)
    {
        var desktop = InputDesktop.Current;
        var element = refs.ResolveRef(reference);
        scrolled =
            desktop.ScrollIntoView(
                element,
                refs.GetProcessIdForRef(reference),
                pending,
                () => refs.ValidateReference(reference)
            ) ?? scrolled;
        return (element, () => desktop.Geometry(element));
    }

    /// <summary>
    /// The ref's own press point (its clickable point, else the centre of its bounds), chosen under the lease
    /// and hit-tested. Choose takes the centre only for an on-screen element with bounds: an offscreen one has
    /// (0,0), a maximized window's caption.
    /// </summary>
    public Func<Point> Centre(string reference, ref string? scrolled) =>
        Pressable(reference, geometry => ClickPoint.Choose(geometry), ref scrolled).Choose;

    /// <summary>Prove a fixed screenshot point lies on this ref without scrolling or moving the point.</summary>
    public Press AtPoint(string reference, Point point)
    {
        var element = Resolve(reference);
        ClickGeometry Read() => InputDesktop.Current.Geometry(element);
        return new(
            () =>
            {
                ClickPoint.Choose(Read(), point);
                ClickPoint.Verify(point, element);
                return point;
            },
            at => Moved(at, element, Read)
        );
    }

    /// <summary>A point on a ref chosen under the lease, then hit-tested: it must land on the ref itself.</summary>
    public Press Pressable(string reference, Func<ClickGeometry, Point> choose, ref string? scrolled)
    {
        var (element, read) = Located(reference, ref scrolled);
        return new(
            () =>
            {
                var point = choose(read());
                ClickPoint.Verify(point, element);
                return point;
            },
            point => Moved(point, element, read)
        );
    }

    /// <summary>Why a point chosen earlier is no longer on the element, or null; a geometry that cannot be re-read counts as moved.</summary>
    private static string? Moved(Point point, AutomationElement element, Func<ClickGeometry> read)
    {
        try
        {
            return ClickPoint.Moved(point, read(), element);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return $"its geometry could not be re-read ({error.Message})";
        }
    }
}
