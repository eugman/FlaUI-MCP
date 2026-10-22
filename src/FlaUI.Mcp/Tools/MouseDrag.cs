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
/// What a drag runs: its points, resolved under the lease, the drop target's own re-check before the release
/// (why it moved, or null), and what UI Automation reports under the pointer after it (null without a ref to
/// ask through).
/// </summary>
internal sealed record DragPlan(
    Func<(Point From, Point To)> Resolve,
    Func<Point, string?> DropMoved,
    Func<Point, string?> DroppedOn,
    Func<Proof>? Resized = null
)
{
    /// <summary>What the drag found after its release: the element under the pointer and a resize's proof.</summary>
    public DragReport Report(Point landed) => new(DroppedOn(landed), Resized?.Invoke());

    /// <summary>
    /// The drop-target-moved refusal with what UI Automation reports under the pointer after the cancel, when a
    /// ref can ask: a mouse-capture drag may have dropped there on the release.
    /// </summary>
    public InputRefusedException AfterCancel(InputRefusedException refused, Point at) =>
        DroppedOn(at) is { } under
            ? InputRefusedException.Record(
                refused.Refusal with
                {
                    Message = $"{refused.Refusal.Message} Under the pointer now: {under}.",
                }
            )
            : refused;
}

/// <summary>What a drag found after its release, for its reply.</summary>
internal sealed record DragReport(string? DroppedOn, Proof? Resized);

/// <summary>Builds a drag's plan from its request: edges, resize targets, drop points and the start's exclusions.</summary>
internal sealed class DragPlanner(RefPoints points)
{
    public DragPlan Plan(DragRequest request, ref string? scrolled)
    {
        var plan = FromRequest(request, ref scrolled);
        return plan with
        {
            Resolve = AvoidingStart(request.FromMustNotHit, plan.Resolve),
            DroppedOn = DroppedOn(request.Reference ?? request.ToRef),
        };
    }

    /// <summary>
    /// What UI Automation reports under a point, as a refusal would name it, read through the drag's ref (the
    /// element under the release is where the drop went). A failed read says so instead of guessing.
    /// </summary>
    private Func<Point, string?> DroppedOn(string? reference)
    {
        if (reference == null)
        {
            return _ => null;
        }

        var anchor = points.Resolve(reference);
        return point =>
        {
            var desktop = InputDesktop.Current;
            try
            {
                var under = desktop.ElementAt(anchor, point);
                return under == null ? "nothing UI Automation can see" : desktop.DescribeElement(under);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                return $"unreadable ({error.Message})";
            }
        };
    }

    /// <summary>
    /// The ref's size after the release, read back through the seam, against the requested one: its width for a
    /// left or right edge, its height for top or bottom. Unavailable when the read fails.
    /// </summary>
    private static Proof Resized(Func<ClickGeometry> read, string edge, int requested)
    {
        var tall = edge is "top" or "bottom";
        string? reached;
        try
        {
            var bounds = read().Bounds;
            reached = (tall ? bounds.Height : bounds.Width).ToString();
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            reached = null;
        }

        return Proof.Compare(
            requested.ToString(),
            reached,
            tall ? "element-height" : "element-width",
            independent: true
        );
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
            Func<Proof>? resized = request.ResizeTo is { } size
                ? () => Resized(read, request.FromEdge ?? "right", size)
                : null;
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
                to?.Moved ?? (_ => null),
                _ => null,
                resized
            );
        }

        var start = StartPoint(request, ref scrolled);
        var drop = DropPoint(request, ref scrolled);
        return new(() => (start.Choose(), drop.Choose()), drop.Moved, _ => null);
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
