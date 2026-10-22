using System.Drawing;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Mcp.Core;

namespace FlaUI.Mcp.Tools;

/// <summary>Guarded pointer actions by window-relative pixel or element reference.</summary>
public sealed partial class MouseTool(ElementRegistry refs, PendingInvokeTracker pending, SessionManager sessions)
    : ToolBase
{
    private const int DefaultHoverMs = 600;
    private const int MaxHoverMs = 5000;
    private const int MaxScrollTicks = 50;

    public override string Name => "windows_mouse";

    public override McpToolAnnotations Annotations => McpToolAnnotations.Input(destructive: true);
    public override string Description =>
        "Move the pointer or click a captured window point. park: move it off the window so no tooltip or hover state "
        + "lands in a screenshot. move/hover: to a point or ref. scroll: wheel ticks over a point or ref "
        + "(positive scrolls up). click: left-click at a point or ref. drag: press at `from` (or ref, or ref's fromEdge), move in steps, release at `to`, toRef/toEdge, "
        + "or wherever resizeTo makes ref that size (a column header: ref + fromEdge right + resizeTo 250). Use it for "
        + "splitters and other targets UI Automation does not expose. Points are pixels in a windows_screenshot "
        + "of the same window (handle), so read them straight off a capture.";

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var action = GetStringArgument(arguments, "action") ?? throw new ArgumentException("action required");
        var reference = GetStringArgument(arguments, "ref");
        var handle =
            GetStringArgument(arguments, "handle") ?? (reference == null ? null : refs.WindowForRef(reference));
        if (handle == null)
        {
            return Task.FromResult(ErrorResult("handle or ref required"));
        }

        if (pending.TryGetPending(sessions.GetWindowProcessId(handle), out var blocked))
        {
            return Task.FromResult(BlockedResult(blocked));
        }

        OperationContext.Check();
        var target = sessions.GetInputTarget(handle);
        var origin = WindowOrigin(handle, target.Hwnd);
        Point landed;
        try
        {
            landed = Act(action, arguments, target, reference, origin);
        }
        catch (Exception error) when (GuardedInput.IsRefusal(error))
        {
            return Task.FromResult(Refused(action, error));
        }

        return Task.FromResult(
            TextResult(
                JsonSerializer.Serialize(
                    new
                    {
                        action,
                        screen = new { x = landed.X, y = landed.Y },
                        window = new { x = landed.X - origin.X, y = landed.Y - origin.Y },
                    }
                )
            )
        );
    }

    /// <summary>
    /// A refusal as windows_click reports one: a failed result, not an exception, so a direct caller sees the
    /// reply the registry sends. The refusal itself stays noted on the operation for the outcome's failure field.
    /// </summary>
    private static McpToolResult Refused(string action, Exception error) =>
        ErrorResult($"Failed to {action}: {error.Message}");

    /// <summary>Run one pointer action; a refusal after a scroll into view says so.</summary>
    private Point Act(string action, JsonElement? arguments, InputTarget target, string? reference, Rectangle origin)
    {
        // Set when resolving a ref scrolled its view, so a later refusal can say so.
        string? scrolled = null;
        try
        {
            return action switch
            {
                "park" => PointerParking.Park(origin),
                "move" => PointerActions.Move(
                    target,
                    ResolvePoint(arguments, "to", reference, origin, ref scrolled),
                    hoverMs: 0
                ),
                "hover" => PointerActions.Move(
                    target,
                    ResolvePoint(arguments, "to", reference, origin, ref scrolled),
                    HoverMs(arguments)
                ),
                "scroll" => PointerActions.Scroll(
                    target,
                    ResolvePoint(arguments, "to", reference, origin, ref scrolled),
                    Ticks(arguments)
                ),
                "click" => PointerActions.Click(target, ResolvePoint(arguments, "to", reference, origin, ref scrolled)),
                "drag" => PointerActions.Drag(
                    target,
                    AvoidingStart(arguments, DragFromArguments(arguments, reference, origin, ref scrolled)),
                    GetArgument<int?>(arguments, "steps") ?? PointerPlan.DefaultDragSteps
                ),
                _ => throw new ArgumentException("action must be park, move, hover, scroll, click or drag"),
            };
        }
        catch (Exception error) when (scrolled != null && GuardedInput.IsRefusal(error))
        {
            throw GuardedInput.ChangedFirst(error, scrolled);
        }
    }

    /// <summary>
    /// Scroll a ref into view through the guards now; the element and a reader for its geometry, which runs
    /// under the input lease.
    /// </summary>
    private (AutomationElement Element, Func<ClickGeometry> Read) Located(string reference, ref string? scrolled)
    {
        var element = refs.ResolveRef(reference);
        scrolled =
            ClickGeometry.ScrollIntoView(
                element,
                refs.GetProcessIdForRef(reference),
                pending,
                () => refs.ValidateReference(reference)
            ) ?? scrolled;
        return (element, () => ClickGeometry.Read(element));
    }

    /// <summary>A point on a ref chosen under the lease, then hit-tested: it must land on the ref itself.</summary>
    private Func<Point> Pressable(string reference, Func<ClickGeometry, Point> choose, ref string? scrolled)
    {
        var (element, read) = Located(reference, ref scrolled);
        return () =>
        {
            var geometry = read();
            var point = choose(geometry);
            ClickPoint.Verify(point, element);
            return point;
        };
    }

    /// <summary>The drag's points, refused under the lease when the start lands on a fromMustNotHit element.</summary>
    private Func<(Point From, Point To)> AvoidingStart(JsonElement? arguments, Func<(Point From, Point To)> resolve)
    {
        var avoid = (GetArgument<string[]>(arguments, "fromMustNotHit") ?? []).Select(r => refs.ResolveRef(r)).ToList();
        if (avoid.Count == 0)
        {
            return resolve;
        }

        return () =>
        {
            var points = resolve();
            ClickHitTest.RequireNotWithin(points.From, avoid[0], avoid);
            return points;
        };
    }

    private Func<(Point From, Point To)> DragFromArguments(
        JsonElement? arguments,
        string? reference,
        Rectangle origin,
        ref string? scrolled
    )
    {
        var fromEdge = GetStringArgument(arguments, "fromEdge");
        var resizeTo = GetArgument<int?>(arguments, "resizeTo");
        var toRef = GetStringArgument(arguments, "toRef");
        if (fromEdge != null || resizeTo != null)
        {
            var (element, read) = Located(
                reference ?? throw new ArgumentException("fromEdge and resizeTo need ref"),
                ref scrolled
            );
            var to = resizeTo == null ? DropPoint(arguments, toRef, origin, ref scrolled) : null;
            return () =>
            {
                var geometry = read();
                var bounds = geometry.Bounds;
                var from = ClickPoint.Choose(geometry, PointerPlan.EdgePoint(bounds, fromEdge ?? "center"));
                // The button goes down here, so the start must be on the ref, not a neighbouring header or pane.
                ClickPoint.Verify(from, element);
                return (from, to?.Invoke() ?? PointerPlan.ResizeTarget(bounds, fromEdge ?? "right", resizeTo!.Value));
            };
        }

        var start = ResolvePoint(arguments, "from", reference, origin, ref scrolled);
        var drop = DropPoint(arguments, toRef, origin, ref scrolled);
        return () => (start(), drop());
    }

    private Func<Point> DropPoint(JsonElement? arguments, string? toRef, Rectangle origin, ref string? scrolled) =>
        toRef != null
            ? EdgeOf(toRef, GetStringArgument(arguments, "toEdge") ?? "center", ref scrolled)
            : ResolvePoint(arguments, "to", null, origin, ref scrolled);

    private Rectangle WindowOrigin(string handle, nint hwnd)
    {
        try
        {
            if (sessions.GetWindow(handle) is { } window)
            {
                return window.BoundingRectangle;
            }
        }
        catch (TimeoutException)
        {
            // A blocked provider: the Win32 bounds are the same rectangle the screen fallback captures.
        }

        return InputDesktop.Current.WindowBounds(hwnd)
            ?? throw new InvalidOperationException("Window bounds unavailable");
    }

    private Func<Point> ResolvePoint(
        JsonElement? arguments,
        string name,
        string? reference,
        Rectangle origin,
        ref string? scrolled
    )
    {
        if (arguments is { } a && a.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Object)
        {
            var point = PointerPlan.ToScreen(
                origin,
                new Point(p.GetProperty("x").GetInt32(), p.GetProperty("y").GetInt32())
            );
            return () => point;
        }

        if (reference == null)
        {
            throw new ArgumentException($"{name} point or ref required");
        }

        // Choose takes the centre only for an on-screen element with bounds; an offscreen one has (0,0), a caption.
        return Pressable(reference, geometry => ClickPoint.Choose(geometry), ref scrolled);
    }

    // The drop point is hit-tested like a press: releasing over an overlay or a sibling drops on that instead.
    private Func<Point> EdgeOf(string reference, string edge, ref string? scrolled) =>
        Pressable(
            reference,
            geometry => ClickPoint.Choose(geometry, PointerPlan.EdgePoint(geometry.Bounds, edge)),
            ref scrolled
        );

    private int HoverMs(JsonElement? arguments) =>
        Math.Clamp(GetArgument<int?>(arguments, "durationMs") ?? DefaultHoverMs, 0, MaxHoverMs);

    private int Ticks(JsonElement? arguments)
    {
        var ticks = GetArgument<int?>(arguments, "ticks") ?? throw new ArgumentException("scroll needs ticks");
        return ticks is 0 or < -MaxScrollTicks or > MaxScrollTicks
            ? throw new ArgumentException($"ticks must be non-zero and within ±{MaxScrollTicks}")
            : ticks;
    }
}
