using System.Drawing;
using FlaUI.Mcp.Core;

/// <summary>
/// The verdict a scene capture takes before and after its grab: the right window in front, owner and popup where they
/// were, the scene unclipped, and nothing above the owner over the scene. Pure, so each refusal can be tested.
/// </summary>
internal static class SceneCheck
{
    /// <summary>What the capture asked for, fixed before the first check.</summary>
    /// <param name="OwnerHwnd">The TE3 main window.</param>
    /// <param name="Dialog">The popup or dialog in the scene.</param>
    /// <param name="Scene">The screen rectangle to grab.</param>
    /// <param name="OwnerBounds">The owner's bounds when the capture began.</param>
    /// <param name="DialogBounds">The popup's bounds when the capture began.</param>
    /// <param name="Modal">A modal dialog must be in front; for a menu popup, the owner.</param>
    /// <param name="IncludeOutsideOwner">The scene is the union of owner and popup rather than inside the owner.</param>
    /// <param name="PopupShadowPadding">The popup's shadow margin, or the default for its kind.</param>
    public sealed record Spec(
        nint OwnerHwnd,
        InputTarget Dialog,
        Rectangle Scene,
        Rectangle OwnerBounds,
        Rectangle DialogBounds,
        bool Modal,
        bool IncludeOutsideOwner = false,
        int? PopupShadowPadding = null
    );

    /// <summary>The desktop as read for one check.</summary>
    /// <param name="Foreground">The foreground window.</param>
    /// <param name="Windows">Top-level windows in z-order, topmost first.</param>
    /// <param name="BoundsOf">A window's bounds, or null when unreadable.</param>
    /// <param name="WorkAreas">Every monitor's work area.</param>
    public sealed record Desktop(
        nint Foreground,
        IReadOnlyList<Win32WindowInfo> Windows,
        Func<nint, Rectangle?> BoundsOf,
        IReadOnlyList<Rectangle> WorkAreas
    );

    /// <summary>Throws InvalidOperationException, naming what is wrong, unless the scene can be grabbed as it is.</summary>
    public static void Verify(Spec spec, Desktop now)
    {
        RequireUnchanged(spec, now);
        RequireUnclipped(spec, now);
        RequireUnobscured(spec, now);
    }

    private static void RequireUnchanged(Spec spec, Desktop now)
    {
        var front = spec.Modal ? spec.Dialog.Hwnd : spec.OwnerHwnd;
        if (
            now.Foreground != front
            || now.BoundsOf(spec.OwnerHwnd) != spec.OwnerBounds
            || now.BoundsOf(spec.Dialog.Hwnd) != spec.DialogBounds
        )
        {
            throw new InvalidOperationException("Scene focus or geometry changed");
        }
    }

    private static void RequireUnclipped(Spec spec, Desktop now)
    {
        var placed = spec.IncludeOutsideOwner
            ? spec.Scene == Rectangle.Union(spec.OwnerBounds, spec.DialogBounds)
            : spec.OwnerBounds.Contains(spec.Scene);
        if (!placed || !now.WorkAreas.Any(area => area.Contains(spec.Scene)))
        {
            throw new InvalidOperationException(
                $"Scene {spec.Scene} is clipped; owner {spec.OwnerBounds}, dialog {spec.DialogBounds}, work areas "
                    + string.Join("; ", now.WorkAreas)
            );
        }
    }

    // Any window above the owner over the scene, other than the popup and its own shadow; unreadable bounds count.
    private static void RequireUnobscured(Spec spec, Desktop now)
    {
        var obstructions = now
            .Windows.TakeWhile(w => w.Hwnd != spec.OwnerHwnd)
            .Where(w => !w.IsCloaked && w.Hwnd != spec.Dialog.Hwnd)
            .Where(w => !IsShadow(spec, w, now.BoundsOf(w.Hwnd)))
            .Where(w => now.BoundsOf(w.Hwnd)?.IntersectsWith(spec.Scene) ?? true)
            .ToArray();
        if (now.Windows.All(w => w.Hwnd != spec.OwnerHwnd) || obstructions.Length > 0)
        {
            throw new InvalidOperationException(
                $"Scene popup {spec.DialogBounds} obscured: "
                    + string.Join(
                        "; ",
                        obstructions.Select(w =>
                            $"HWND={w.Hwnd}, PID={w.ProcessId}, title={w.Title}, bounds={now.BoundsOf(w.Hwnd)}"
                        )
                    )
            );
        }
    }

    private static bool IsShadow(Spec spec, Win32WindowInfo window, Rectangle? bounds) =>
        Te3Page.IsPopupShadow(
            window,
            bounds,
            spec.Dialog.ProcessId,
            spec.DialogBounds,
            spec.Modal,
            spec.PopupShadowPadding
        );
}
