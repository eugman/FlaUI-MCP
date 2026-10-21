using System.Drawing;
using System.Text.Json;
using FlaUI.Core.Input;
using FlaUI.Mcp.Core;

namespace FlaUI.Mcp.Tools;

/// <summary>Guarded pointer actions by window-relative pixel or element reference.</summary>
public sealed class MouseTool(ElementRegistry refs, PendingInvokeTracker pending, SessionManager sessions) : ToolBase
{
    private const int DefaultHoverMs = 600;
    private const int MaxHoverMs = 5000;
    private const int MaxScrollTicks = 50;
    private static readonly TimeSpan StepDelay = TimeSpan.FromMilliseconds(15);

    public override string Name => "windows_mouse";

    public override McpToolAnnotations Annotations => McpToolAnnotations.Input(destructive: true);
    public override string Description =>
        "Move the pointer or click a captured window point. park: move it off the window so no tooltip or hover state "
        + "lands in a screenshot. move/hover: to a point or ref. scroll: wheel ticks over a point or ref "
        + "(positive scrolls up). click: left-click at a point or ref. drag: press at `from` (or ref, or ref's fromEdge), move in steps, release at `to`, toRef/toEdge, "
        + "or wherever resizeTo makes ref that size (a column header: ref + fromEdge right + resizeTo 250). Use it for "
        + "splitters and other targets UI Automation does not expose. Points are pixels in a windows_screenshot "
        + "of the same window (handle), so read them straight off a capture.";
    public override object InputSchema =>
        new
        {
            type = "object",
            properties = new
            {
                action = new { type = "string", @enum = new[] { "park", "move", "hover", "scroll", "drag", "click" } },
                handle = new { type = "string", description = "Window handle; points are relative to its capture." },
                @ref = new { type = "string", description = "Element to move to, hover, scroll over, or drag from." },
                from = PointSchema("Drag start in window pixels (alternative to ref)."),
                to = PointSchema("Target in window pixels: move/hover/scroll/click point, or drag end."),
                ticks = new { type = "integer", description = "scroll: wheel ticks, positive is up." },
                durationMs = new { type = "integer", description = "hover: how long to rest (default 600)." },
                steps = new { type = "integer", description = "drag: intermediate moves (default 12)." },
                fromEdge = new
                {
                    type = "string",
                    @enum = PointerPlan.Edges,
                    description = "drag: grab this edge of ref instead of its centre (e.g. right for a column header's divider).",
                },
                toRef = new { type = "string", description = "drag: release on this element instead of a `to` point." },
                toEdge = new
                {
                    type = "string",
                    @enum = PointerPlan.Edges,
                    description = "drag: which part of toRef to release on (default center; left drops a column header before it).",
                },
                resizeTo = new
                {
                    type = "integer",
                    description = "drag with ref + fromEdge: release where ref becomes this many pixels wide (left/right) or tall (top/bottom). One call, no screenshot needed.",
                },
            },
            required = new[] { "action" },
        };

    private static object PointSchema(string description) =>
        new
        {
            type = "object",
            description,
            properties = new { x = new { type = "integer" }, y = new { type = "integer" } },
            required = new[] { "x", "y" },
        };

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
        // Set when resolving a ref scrolled its view, so a later refusal can say so.
        string? scrolled = null;
        Point landed;
        try
        {
            landed = action switch
            {
                "park" => PointerParking.Park(origin),
                "move" => Move(target, ResolvePoint(arguments, "to", reference, origin, ref scrolled), hoverMs: 0),
                "hover" => Move(
                    target,
                    ResolvePoint(arguments, "to", reference, origin, ref scrolled),
                    HoverMs(arguments)
                ),
                "scroll" => Scroll(
                    target,
                    ResolvePoint(arguments, "to", reference, origin, ref scrolled),
                    Ticks(arguments)
                ),
                "click" => Click(target, ResolvePoint(arguments, "to", reference, origin, ref scrolled)),
                "drag" => DragFromArguments(arguments, target, reference, origin, ref scrolled),
                _ => throw new ArgumentException("action must be park, move, hover, scroll, click or drag"),
            };
        }
        catch (InvalidOperationException error) when (scrolled != null)
        {
            throw GuardedInput.ChangedFirst(error, scrolled);
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

    /// <summary>Scroll a ref into view through the guards, then read where it is.</summary>
    private ClickGeometry Geometry(string reference, ref string? scrolled)
    {
        var element = refs.ResolveRef(reference);
        scrolled =
            ClickGeometry.ScrollIntoView(
                element,
                refs.GetProcessIdForRef(reference),
                pending,
                () => refs.ValidateReference(reference)
            ) ?? scrolled;
        return ClickGeometry.Read(element);
    }

    private Point DragFromArguments(
        JsonElement? arguments,
        InputTarget target,
        string? reference,
        Rectangle origin,
        ref string? scrolled
    )
    {
        var fromEdge = GetStringArgument(arguments, "fromEdge");
        var resizeTo = GetArgument<int?>(arguments, "resizeTo");
        var toRef = GetStringArgument(arguments, "toRef");
        Point from;
        Point to;
        if (fromEdge != null || resizeTo != null)
        {
            var geometry = Geometry(
                reference ?? throw new ArgumentException("fromEdge and resizeTo need ref"),
                ref scrolled
            );
            var bounds = geometry.Bounds;
            from = ClickPoint.Choose(geometry, PointerPlan.EdgePoint(bounds, fromEdge ?? "center"));
            to = resizeTo is { } size ? PointerPlan.ResizeTarget(bounds, fromEdge ?? "right", size) : default;
        }
        else
        {
            from = ResolvePoint(arguments, "from", reference, origin, ref scrolled);
            to = default;
        }

        if (resizeTo == null)
        {
            to =
                toRef != null
                    ? EdgeOf(toRef, GetStringArgument(arguments, "toEdge") ?? "center", ref scrolled)
                    : ResolvePoint(arguments, "to", null, origin, ref scrolled);
        }

        return Drag(target, from, to, GetArgument<int?>(arguments, "steps") ?? PointerPlan.DefaultDragSteps);
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

        return Win32Desktop.GetWindowBounds(hwnd) ?? throw new InvalidOperationException("Window bounds unavailable");
    }

    private Point ResolvePoint(
        JsonElement? arguments,
        string name,
        string? reference,
        Rectangle origin,
        ref string? scrolled
    )
    {
        if (arguments is { } a && a.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Object)
        {
            return PointerPlan.ToScreen(
                origin,
                new Point(p.GetProperty("x").GetInt32(), p.GetProperty("y").GetInt32())
            );
        }

        if (reference == null)
        {
            throw new ArgumentException($"{name} point or ref required");
        }

        // Never the centre of the bounds: for an offscreen element that is (0,0), a maximized window's caption.
        return ClickPoint.Choose(Geometry(reference, ref scrolled));
    }

    private Point EdgeOf(string reference, string edge, ref string? scrolled)
    {
        var geometry = Geometry(reference, ref scrolled);
        return ClickPoint.Choose(geometry, PointerPlan.EdgePoint(geometry.Bounds, edge));
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

    private static Point Move(InputTarget target, Point point, int hoverMs)
    {
        using var input = new GuardedInput(target);
        input.Check(() => RequireHit(target, point));
        input.Send(() => Mouse.MoveTo(point));
        if (hoverMs > 0)
        {
            Thread.Sleep(hoverMs);
        }

        return Mouse.Position;
    }

    private static Point Scroll(InputTarget target, Point point, int ticks)
    {
        using var input = new GuardedInput(target);
        input.Check(() => RequireHit(target, point));
        input.Send(() => Mouse.MoveTo(point));
        input.Send(() => Mouse.Scroll(ticks));
        return Mouse.Position;
    }

    private static Point Click(InputTarget target, Point point)
    {
        using var input = new GuardedInput(target);
        input.Check(() =>
            ClickGuarded(target, point, clicked => input.Send(() => Mouse.Click(clicked, MouseButton.Left)))
        );
        return Mouse.Position;
    }

    internal static void ClickGuarded(
        InputTarget target,
        Point point,
        Action<Point> click,
        Func<Point, nint>? windowAt = null
    )
    {
        RequireHit(target, point, windowAt);
        click(point);
    }

    internal static Point Drag(InputTarget target, Point from, Point to, int steps)
    {
        var path = PointerPlan.DragPath(from, to, steps);
        using var input = new GuardedInput(target);
        input.Check(() => RequireHit(target, from));
        // The release lands at `to`; refuse a drag that would drop onto another window or the desktop.
        input.Check(() => RequireHit(target, to));
        input.Send(() => Mouse.MoveTo(from));
        input.Send(() => Mouse.Down(MouseButton.Left));
        try
        {
            foreach (var point in path)
            {
                input.Send(() => Mouse.MoveTo(point));
                Thread.Sleep(StepDelay);
            }
        }
        finally
        {
            // Never leave the button held, even when a step fails verification.
            input.Release(() => Mouse.Up(MouseButton.Left));
        }

        return Mouse.Position;
    }

    internal static void RequireHit(InputTarget target, Point point, Func<Point, nint>? windowAt = null)
    {
        var root = target.HitHwnd == 0 ? target.Hwnd : target.HitHwnd;
        var under = (windowAt ?? InputDesktop.Current.WindowAt)(point);
        if (under != root)
        {
            throw GuardedInput.NotOnTarget(
                target,
                under,
                "Point is not on the target window (off-window or covered); no input sent."
            );
        }
    }
}
