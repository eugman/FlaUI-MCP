using System.Drawing;
using FlaUI.Core.Input;
using FlaUI.Mcp.Core;

namespace FlaUI.Mcp.Tools;

/// <summary>
/// The guarded pointer input behind windows_mouse. Each action takes guarded input first, then resolves its
/// points under the lease, checks they are on the target window, and only then moves or presses.
/// </summary>
internal static class PointerActions
{
    private static readonly TimeSpan StepDelay = TimeSpan.FromMilliseconds(15);

    internal static Point Move(InputTarget target, Func<Point> resolve, int hoverMs)
    {
        using var input = new GuardedInput(target);
        var point = default(Point);
        input.Check(() => RequireHit(target, point = resolve()));
        input.Send(() => Mouse.MoveTo(point));
        if (hoverMs > 0)
        {
            Thread.Sleep(hoverMs);
        }

        return Mouse.Position;
    }

    internal static Point Scroll(InputTarget target, Func<Point> resolve, int ticks)
    {
        using var input = new GuardedInput(target);
        var point = default(Point);
        input.Check(() => RequireHit(target, point = resolve()));
        input.Send(() => Mouse.MoveTo(point));
        input.Send(() => Mouse.Scroll(ticks));
        return Mouse.Position;
    }

    internal static Point Click(InputTarget target, Func<Point> resolve)
    {
        using var input = new GuardedInput(target);
        input.Check(() =>
            ClickGuarded(target, resolve(), clicked => input.Send(() => Mouse.Click(clicked, MouseButton.Left)))
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

    internal static Point Drag(InputTarget target, Point from, Point to, int steps) =>
        Drag(target, () => (from, to), steps);

    internal static Point Drag(InputTarget target, Func<(Point From, Point To)> resolve, int steps)
    {
        // Validate the step count before any focus change.
        PointerPlan.DragPath(Point.Empty, Point.Empty, steps);
        using var input = new GuardedInput(target);
        var (from, to) = (Point.Empty, Point.Empty);
        input.Check(() => (from, to) = resolve());
        var path = PointerPlan.DragPath(from, to, steps);
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
