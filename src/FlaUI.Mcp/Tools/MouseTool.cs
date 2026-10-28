using System.Drawing;
using System.Text.Json;
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

    private readonly RefPoints points = new(
        refs,
        pending,
        reference => sessions.GetInputTarget(refs.WindowForRef(reference))
    );

    public override string Name => "windows_mouse";

    public override McpToolAnnotations Annotations => McpToolAnnotations.Input(destructive: true);
    public override string Description =>
        "Move the pointer or click a captured window point. park: try to move it off the window; this can be refused "
        + "or leave hover state behind, so inspect the capture. move/hover: to a point or ref. scroll: wheel ticks over a point or ref "
        + "(positive scrolls up). click: left- or right-click at a point or ref (`button` defaults to left). drag: press at `from` (or ref, or ref's fromEdge), move in steps, release at `to`, toRef/toEdge, "
        + "or wherever resizeTo makes ref that size (a column header: ref + fromEdge right + resizeTo 250). Use it for "
        + "splitters and other targets UI Automation does not expose. Points are pixels in a windows_screenshot "
        + "of the same window (handle), so read them straight off a capture.";

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var action = GetStringArgument(arguments, "action") ?? throw new ArgumentException("action required");
        if (action == "click")
        {
            try
            {
                ClickButton(GetStringArgument(arguments, "button"));
            }
            catch (ArgumentException error)
            {
                return Task.FromResult(ErrorResult(RefusalCodes.InvalidArguments, error.Message));
            }
        }

        var reference = GetStringArgument(arguments, "ref");
        var handle =
            GetStringArgument(arguments, "handle") ?? (reference == null ? null : refs.WindowForRef(reference));
        if (handle == null)
        {
            return Task.FromResult(ErrorResult(RefusalCodes.InvalidArguments, "handle or ref required"));
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
        // What a drag found after its release: the element under the pointer, a resize's proof.
        DragReport? drag = null;
        Point landed;
        try
        {
            landed = Act(action, arguments, target, handle, origin, ref scrolled, out drag);
        }
        catch (Exception error) when (GuardedInput.IsRefusal(error))
        {
            return Task.FromResult(Refused(action, error));
        }
        catch (ArgumentException error)
            when (action == "drag" && ((OperationContext.Current.Value?.InputsSent ?? 0) == 0))
        {
            return Task.FromResult(ErrorResult(RefusalCodes.InvalidArguments, $"Failed to {action}: {error.Message}"));
        }

        return Task.FromResult(MouseReplies.Landed(action, landed, origin, scrolled, drag));
    }

    /// <summary>
    /// A refusal as windows_click reports one: a failed result, not an exception, so a direct caller sees the
    /// reply the registry sends. The refusal itself stays noted on the operation for the outcome's failure field.
    /// A drag whose drop target moved is not failed but unverified: every input went out, and whether the app
    /// dropped on the release is unknown.
    /// </summary>
    private static McpToolResult Refused(string action, Exception error) =>
        error
            is InputRefusedException
            {
                Refusal.Code: RefusalCodes.DropTargetMoved or RefusalCodes.DragInterrupted,
            } moved
            ? MouseReplies.Unverified(moved.Refusal)
            : ErrorResult($"Failed to {action}: {error.Message}");

    /// <summary>Run one pointer action; a refusal after a scroll into view says so.</summary>
    private Point Act(
        string action,
        JsonElement? arguments,
        InputTarget target,
        string handle,
        Rectangle origin,
        ref string? scrolled,
        out DragReport? drag
    )
    {
        drag = null;
        var reference = GetStringArgument(arguments, "ref");
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
                "click" => PointerActions.Click(
                    target,
                    ResolvePoint(arguments, "to", reference, origin, ref scrolled),
                    ClickButton(GetStringArgument(arguments, "button"))
                ),
                "drag" => Drag(arguments, target, reference, handle, origin, ref scrolled, out drag),
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
        string handle,
        Rectangle origin,
        ref string? scrolled,
        out DragReport drag
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
        )
        {
            FromMustHit = GetStringArgument(arguments, "fromMustHit"),
            ToMustHit = GetStringArgument(arguments, "toMustHit"),
        };
        var plan = new DragPlanner(points).Plan(request, handle, ref scrolled);
        if (request.From != null || request.To != null)
        {
            plan = plan with { BeforeMove = () => RequireSameOrigin(handle, target.Hwnd, origin) };
        }

        try
        {
            return plan.Execute(target, request.Steps, out drag);
        }
        catch (InputRefusedException refused)
            when (refused.Refusal.Code is RefusalCodes.DropTargetMoved or RefusalCodes.DragInterrupted)
        {
            // The release may have dropped anyway: say what is under the pointer now, as a completed drag does.
            throw plan.AfterCancel(refused, InputDesktop.Current.PointerPosition());
        }
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

    private void RequireSameOrigin(string handle, nint hwnd, Rectangle origin)
    {
        if (WindowOrigin(handle, hwnd) != origin)
        {
            PointerActions.RefuseChangedBounds();
        }
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

    internal static MouseButton ClickButton(string? button) =>
        button switch
        {
            null or "left" => MouseButton.Left,
            "right" => MouseButton.Right,
            _ => throw new ArgumentException("button must be left or right"),
        };

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
