using System.Drawing;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
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
        input.MoveTo(point);
        if (hoverMs > 0)
        {
            Thread.Sleep(hoverMs);
        }

        return InputDesktop.Current.PointerPosition();
    }

    internal static Point Scroll(InputTarget target, Func<Point> resolve, int ticks)
    {
        using var input = new GuardedInput(target);
        var point = default(Point);
        input.Check(() => RequireHit(target, point = resolve()));
        input.MoveTo(point);
        input.Send(() => Mouse.Scroll(ticks));
        return InputDesktop.Current.PointerPosition();
    }

    internal static Point Click(InputTarget target, Func<Point> resolve)
    {
        using var input = new GuardedInput(target);
        input.Check(() =>
            ClickGuarded(target, resolve(), clicked => input.Send(() => Mouse.Click(clicked, MouseButton.Left)))
        );
        return InputDesktop.Current.PointerPosition();
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

    /// <summary>
    /// Press at the resolved start, move along the path and release at the resolved end. Before the release the
    /// end is checked again: it must still be on the target window, and <paramref name="dropMoved"/> (the drop
    /// target's own re-read, when there is one) must find nothing moved; otherwise the drag is cancelled with
    /// Escape while the button is still down, then released, and refused as drop-target-moved.
    /// </summary>
    internal static Point Drag(
        InputTarget target,
        Func<(Point From, Point To)> resolve,
        int steps,
        Func<Point, string?>? dropMoved = null
    )
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
        input.MoveTo(from);
        input.Send(() => Mouse.Down(MouseButton.Left));
        string? moved = null;
        try
        {
            foreach (var point in path)
            {
                input.MoveTo(point);
                Thread.Sleep(StepDelay);
            }

            // Whatever is under `to` now takes the drop, and the app may have scrolled or re-laid out meanwhile
            // (a tree auto-scrolls while a drag hovers its first row).
            moved = DropMoved(target, to, dropMoved);
            if (moved != null)
            {
                input.Release(() => Keyboard.Type(VirtualKeyShort.ESCAPE));
            }
        }
        finally
        {
            // Never leave the button held, even when a step fails verification.
            input.Release(() => Mouse.Up(MouseButton.Left));
        }

        return moved == null ? InputDesktop.Current.PointerPosition() : throw Cancelled(moved);
    }

    /// <summary>Why the drop point no longer lands on what it was proved on, or null.</summary>
    private static string? DropMoved(InputTarget target, Point to, Func<Point, string?>? dropMoved)
    {
        var root = target.HitHwnd == 0 ? target.Hwnd : target.HitHwnd;
        if (InputDesktop.Current.WindowAt(to) != root)
        {
            return $"the drop point ({to.X},{to.Y}) is no longer on the target window";
        }

        return dropMoved?.Invoke(to);
    }

    private static InputRefusedException Cancelled(string moved) =>
        InputRefusedException.Record(
            new InputRefusal(
                RefusalCodes.DropTargetMoved,
                $"The drop target moved during the drag: {moved}. The drag was cancelled with Escape before the "
                    + "button was released, so nothing was dropped; observe the window, then retry."
            )
            {
                ActionDispatched = true,
            }
        );

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
