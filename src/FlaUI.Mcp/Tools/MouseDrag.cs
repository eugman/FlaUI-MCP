using System.Drawing;
using FlaUI.Mcp.Core;

namespace FlaUI.Mcp.Tools;

/// <summary>A windows_mouse drag as the tool read it; window-relative points are already in screen pixels.</summary>
internal sealed record DragRequest(
    string? Reference,
    Point? From,
    string? FromEdge,
    int? ResizeTo,
    Point? To,
    string? ToRef,
    string ToEdge,
    string[] FromMustNotHit,
    int Steps
);

/// <summary>
/// What a drag runs: its points, resolved under the lease, and the drop target's own re-check before the release
/// (why it moved, or null).
/// </summary>
internal sealed record DragPlan(Func<(Point From, Point To)> Resolve, Func<Point, string?> DropMoved);

/// <summary>Builds a drag's plan from its request: edges, resize targets, drop points and the start's exclusions.</summary>
internal sealed class DragPlanner(RefPoints points)
{
    public DragPlan Plan(DragRequest request, ref string? scrolled)
    {
        var plan = FromRequest(request, ref scrolled);
        return plan with { Resolve = AvoidingStart(request.FromMustNotHit, plan.Resolve) };
    }

    private DragPlan FromRequest(DragRequest request, ref string? scrolled)
    {
        if (request.FromEdge != null || request.ResizeTo != null)
        {
            var (element, read) = points.Located(
                request.Reference ?? throw new ArgumentException("fromEdge and resizeTo need ref"),
                ref scrolled
            );
            var to = request.ResizeTo == null ? DropPoint(request, ref scrolled) : null;
            return new(
                () =>
                {
                    var geometry = read();
                    var bounds = geometry.Bounds;
                    var from = ClickPoint.Choose(geometry, PointerPlan.EdgePoint(bounds, request.FromEdge ?? "center"));
                    // The button goes down here, so the start must be on the ref, not a neighbouring header or pane.
                    ClickPoint.Verify(from, element);
                    return (
                        from,
                        to?.Choose()
                            ?? PointerPlan.ResizeTarget(bounds, request.FromEdge ?? "right", request.ResizeTo!.Value)
                    );
                },
                to?.Moved ?? (_ => null)
            );
        }

        var start = StartPoint(request, ref scrolled);
        var drop = DropPoint(request, ref scrolled);
        return new(() => (start.Choose(), drop.Choose()), drop.Moved);
    }

    private Press StartPoint(DragRequest request, ref string? scrolled) =>
        request.From is { } from
            ? Press.Fixed(from)
            : points.Pressable(
                request.Reference ?? throw new ArgumentException("from point or ref required"),
                geometry => ClickPoint.Choose(geometry),
                ref scrolled
            );

    private Press DropPoint(DragRequest request, ref string? scrolled)
    {
        if (request.ToRef != null)
        {
            return EdgeOf(request.ToRef, request.ToEdge, ref scrolled);
        }

        return Press.Fixed(request.To ?? throw new ArgumentException("to point or ref required"));
    }

    // The drop point is hit-tested like a press: releasing over an overlay or a sibling drops on that instead. It
    // sits DropInset pixels inside the edge, off the neighbour's border, and never where a resting drag scrolls.
    private Press EdgeOf(string reference, string edge, ref string? scrolled) =>
        points.Pressable(
            reference,
            geometry =>
            {
                var point = ClickPoint.Choose(
                    geometry,
                    PointerPlan.EdgePoint(geometry.Bounds, edge, PointerPlan.DropInset)
                );
                ClickPoint.RequireOutsideScrollBand(point, geometry);
                return point;
            },
            ref scrolled
        );

    /// <summary>The drag's points, refused under the lease when the start lands on a fromMustNotHit element.</summary>
    private Func<(Point From, Point To)> AvoidingStart(string[] mustNotHit, Func<(Point From, Point To)> resolve)
    {
        var avoid = mustNotHit.Select(points.Resolve).ToList();
        if (avoid.Count == 0)
        {
            return resolve;
        }

        return () =>
        {
            var resolved = resolve();
            ClickHitTest.RequireNotWithin(resolved.From, avoid[0], avoid);
            return resolved;
        };
    }
}
