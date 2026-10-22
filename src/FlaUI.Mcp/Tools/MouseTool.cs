using System.Drawing;
using System.Text.Json;
using FlaUI.Mcp.Core;

namespace FlaUI.Mcp.Tools;

/// <summary>Guarded pointer actions by window-relative pixel or element reference.</summary>
public sealed partial class MouseTool(ElementRegistry refs, PendingInvokeTracker pending, SessionManager sessions)
    : ToolBase
{
    private const int DefaultHoverMs = 600;
    private const int MaxHoverMs = 5000;
    private const int MaxScrollTicks = 50;

    private readonly RefPoints points = new(refs, pending);

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
        // Set when resolving a ref scrolled its view, so the reply (or a refusal) can say so.
        string? scrolled = null;
        // What a drag found under the pointer after its release, when it could ask.
        string? droppedOn = null;
        Point landed;
        try
        {
            landed = Act(action, arguments, target, reference, origin, ref scrolled, out droppedOn);
        }
        catch (Exception error) when (GuardedInput.IsRefusal(error))
        {
            return Task.FromResult(Refused(action, error));
        }

        return Task.FromResult(TextResult(MouseReplies.Landed(action, landed, origin, scrolled, droppedOn)));
    }

    /// <summary>
    /// A refusal as windows_click reports one: a failed result, not an exception, so a direct caller sees the
    /// reply the registry sends. The refusal itself stays noted on the operation for the outcome's failure field.
    /// </summary>
    private static McpToolResult Refused(string action, Exception error) =>
        ErrorResult($"Failed to {action}: {error.Message}");

    /// <summary>Run one pointer action; a refusal after a scroll into view says so.</summary>
    private Point Act(
        string action,
        JsonElement? arguments,
        InputTarget target,
        string? reference,
        Rectangle origin,
        ref string? scrolled,
        out string? droppedOn
    )
    {
        droppedOn = null;
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
                "drag" => Drag(arguments, target, reference, origin, ref scrolled, out droppedOn),
                _ => throw new ArgumentException("action must be park, move, hover, scroll, click or drag"),
            };
        }
        catch (Exception error) when (scrolled != null && GuardedInput.IsRefusal(error))
        {
            throw GuardedInput.ChangedFirst(error, scrolled);
        }
    }

    private Point Drag(
        JsonElement? arguments,
        InputTarget target,
        string? reference,
        Rectangle origin,
        ref string? scrolled,
        out string? droppedOn
    )
    {
        var request = new DragRequest(
            reference,
            PointArgument(arguments, "from", origin),
            GetStringArgument(arguments, "fromEdge"),
            GetArgument<int?>(arguments, "resizeTo"),
            PointArgument(arguments, "to", origin),
            GetStringArgument(arguments, "toRef"),
            GetStringArgument(arguments, "toEdge") ?? "center",
            GetArgument<string[]>(arguments, "fromMustNotHit") ?? [],
            GetArgument<int?>(arguments, "steps") ?? PointerPlan.DefaultDragSteps
        );
        var plan = new DragPlanner(points).Plan(request, ref scrolled);
        var landed = PointerActions.Drag(target, plan.Resolve, request.Steps, plan.DropMoved);
        droppedOn = plan.DroppedOn(landed);
        return landed;
    }

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

    /// <summary>A window-relative point argument as a screen point, or null when it is not given.</summary>
    private static Point? PointArgument(JsonElement? arguments, string name, Rectangle origin) =>
        arguments is { } a && a.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Object
            ? PointerPlan.ToScreen(origin, new Point(p.GetProperty("x").GetInt32(), p.GetProperty("y").GetInt32()))
            : null;

    private Func<Point> ResolvePoint(
        JsonElement? arguments,
        string name,
        string? reference,
        Rectangle origin,
        ref string? scrolled
    )
    {
        if (PointArgument(arguments, name, origin) is { } point)
        {
            return () => point;
        }

        if (reference == null)
        {
            throw new ArgumentException($"{name} point or ref required");
        }

        return points.Centre(reference, ref scrolled);
    }

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
