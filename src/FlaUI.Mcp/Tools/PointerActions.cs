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
    /// target's own re-read, when there is one) must find nothing moved. Whatever goes wrong once the button is
    /// down (the target moved, a step refused, a provider error, a cancellation) takes one way out: Escape, sent
    /// only while the target still holds the foreground (an OLE drag ends without a drop on it; a mouse-capture
    /// drag may still drop on the release), then the release, then the typed refusal saying what went out.
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
        var (moved, failure) = Held(input, target, path, to, dropMoved);
        if (moved == null && failure == null)
        {
            input.Release(() => Mouse.Up(MouseButton.Left));
            return InputDesktop.Current.PointerPosition();
        }

        throw Ended(moved, failure, LetGo(input));
    }

    /// <summary>
    /// The moves with the button down, then the drop re-check: why the drop target moved, or what failed. Whatever
    /// is under `to` now takes the drop, and the app may have scrolled or re-laid out meanwhile (a tree
    /// auto-scrolls while a drag hovers its first row).
    /// </summary>
    private static (string? Moved, Exception? Failure) Held(
        GuardedInput input,
        InputTarget target,
        IReadOnlyList<Point> path,
        Point to,
        Func<Point, string?>? dropMoved
    )
    {
        try
        {
            foreach (var point in path)
            {
                input.MoveTo(point);
                Thread.Sleep(StepDelay);
            }

            return (DropMoved(target, to, dropMoved), null);
        }
        catch (Exception error)
        {
            // Every failure with the button down, a cancellation included, must still let go of it.
            return (null, error);
        }
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

    /// <summary>
    /// Escape, through the checked send so it never reaches a window that took the foreground, then the release,
    /// which goes out regardless; what each did, for the refusal.
    /// </summary>
    private static string LetGo(GuardedInput input)
    {
        string escape;
        try
        {
            input.Send(() => Keyboard.Type(VirtualKeyShort.ESCAPE));
            escape = "Escape was sent";
        }
        catch (Exception error)
        {
            escape = $"Escape was not sent ({(error is AutomationRefusal refused ? refused.Code : error.Message)})";
        }

        string release;
        try
        {
            input.Release(() => Mouse.Up(MouseButton.Left));
            release = "the button was released";
        }
        catch (Exception error)
        {
            release = $"the release failed ({error.Message})";
        }

        return $"{escape} and {release}";
    }

    /// <summary>
    /// The typed refusal for a drag that ended badly, noted on the operation with what went out: drop-target-moved
    /// when the target moved, the step's own refusal otherwise, drag-interrupted for a provider error, and for a
    /// cancellation the cancellation itself (its refusal stays noted for the outcome's failure field).
    /// </summary>
    private static Exception Ended(string? moved, Exception? failure, string letGo)
    {
        var tail = $" {letGo}; whether the app dropped is not verified: observe the window, then retry.";
        var refusal = (moved, failure) switch
        {
            (not null, _) => new InputRefusal(
                RefusalCodes.DropTargetMoved,
                $"The drop target moved during the drag: {moved}.{tail}"
            ),
            (_, InputRefusedException refused) => refused.Refusal with { Message = refused.Message + tail },
            (_, AutomationRefusal other) => new InputRefusal(other.Code, other.Message + tail),
            (_, OperationCanceledException) => new InputRefusal(
                RefusalCodes.DragInterrupted,
                $"The drag was cancelled while the button was down.{tail}"
            ),
            _ => new InputRefusal(
                RefusalCodes.DragInterrupted,
                $"The drag failed while the button was down ({failure!.Message}).{tail}"
            ),
        };
        var recorded = InputRefusedException.Record(refusal with { ActionDispatched = true });
        return failure is OperationCanceledException ? failure : recorded;
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
