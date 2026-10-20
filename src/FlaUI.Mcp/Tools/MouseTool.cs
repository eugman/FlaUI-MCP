using System.Drawing;
using System.Text.Json;
using FlaUI.Core.Input;
using FlaUI.Mcp.Core;

namespace FlaUI.Mcp.Tools;

/// <summary>Pointer moves that windows_click cannot express: park, move, hover, scroll and drag.</summary>
public sealed class MouseTool(ElementRegistry refs, PendingInvokeTracker pending, SessionManager sessions) : ToolBase
{
    private const int DefaultHoverMs = 600;
    private const int MaxHoverMs = 5000;
    private const int MaxScrollTicks = 50;
    private static readonly TimeSpan StepDelay = TimeSpan.FromMilliseconds(15);

    public override string Name => "windows_mouse";
    public override string Description =>
        "Move the pointer without clicking. park: move it off the window so no tooltip or hover state "
        + "lands in a screenshot. move/hover: to a point or ref. scroll: wheel ticks over a point or ref "
        + "(positive scrolls up). drag: press at `from` (or ref), move in steps, release at `to`; use it for "
        + "splitters and other targets UI Automation does not expose. Points are pixels in a windows_screenshot "
        + "of the same window (handle), so read them straight off a capture.";
    public override object InputSchema =>
        new
        {
            type = "object",
            properties = new
            {
                action = new { type = "string", @enum = new[] { "park", "move", "hover", "scroll", "drag" } },
                handle = new { type = "string", description = "Window handle; points are relative to its capture." },
                @ref = new { type = "string", description = "Element to move to, hover, scroll over, or drag from." },
                from = PointSchema("Drag start in window pixels (alternative to ref)."),
                to = PointSchema("Target in window pixels: move/hover/scroll point, or drag end."),
                ticks = new { type = "integer", description = "scroll: wheel ticks, positive is up." },
                durationMs = new { type = "integer", description = "hover: how long to rest (default 600)." },
                steps = new { type = "integer", description = "drag: intermediate moves (default 12)." },
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
        var landed = action switch
        {
            "park" => PointerParking.Park(origin),
            "move" => Move(target, ResolvePoint(arguments, "to", reference, origin), hoverMs: 0),
            "hover" => Move(target, ResolvePoint(arguments, "to", reference, origin), HoverMs(arguments)),
            "scroll" => Scroll(target, ResolvePoint(arguments, "to", reference, origin), Ticks(arguments)),
            "drag" => Drag(
                target,
                ResolvePoint(arguments, "from", reference, origin),
                ResolvePoint(arguments, "to", null, origin),
                GetArgument<int?>(arguments, "steps") ?? PointerPlan.DefaultDragSteps
            ),
            _ => throw new ArgumentException("action must be park, move, hover, scroll or drag"),
        };
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

    private Point ResolvePoint(JsonElement? arguments, string name, string? reference, Rectangle origin)
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

        var element = refs.ResolveRef(reference);
        if (element.TryGetClickablePoint(out var clickable))
        {
            return clickable;
        }

        var bounds = element.BoundingRectangle;
        return new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
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
        RequireHit(target, point);
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
        RequireHit(target, point);
        input.Send(() => Mouse.MoveTo(point));
        input.Send(() => Mouse.Scroll(ticks));
        return Mouse.Position;
    }

    private static Point Drag(InputTarget target, Point from, Point to, int steps)
    {
        var path = PointerPlan.DragPath(from, to, steps);
        using var input = new GuardedInput(target);
        RequireHit(target, from);
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
            Mouse.Up(MouseButton.Left);
        }

        return Mouse.Position;
    }

    private static void RequireHit(InputTarget target, Point point)
    {
        var root = target.HitHwnd == 0 ? target.Hwnd : target.HitHwnd;
        if (Win32Desktop.WindowAt(point) != root)
        {
            throw new InvalidOperationException(
                "Point is not on the target window (off-window or covered); no input sent."
            );
        }
    }
}
