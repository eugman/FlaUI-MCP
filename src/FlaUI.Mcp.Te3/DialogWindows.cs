using FlaUI.Mcp.Core;

/// <summary>
/// The dialog a TE3 action opened, and whether one closed, judged from the process's top-level windows: a dialog is
/// new since the action, titled, owned by TE3's main window and of a class that can be a dialog (Core's
/// WindowBaseline.CanBeDialog: not a tooltip, menu, tool window or cloaked window).
/// </summary>
internal static class DialogWindows
{
    /// <summary>
    /// The one new dialog, preferring those TE3's main window owns (a window owned elsewhere counts only when none is);
    /// null while none has opened; refused when several could be it. Which TE3 dialogs are owned by the main window is
    /// not yet mapped live (UI-MAP), hence the fallback.
    /// </summary>
    public static Win32WindowInfo? NewDialog(
        IEnumerable<Win32WindowInfo> now,
        IReadOnlyCollection<nint> before,
        nint main
    )
    {
        var opened = now.Where(w => !before.Contains(w.Hwnd) && w.Title.Length > 0 && WindowBaseline.CanBeDialog(w))
            .ToList();
        var owned = opened.Where(w => w.Owner == main).ToList();
        var candidates = owned.Count > 0 ? owned : opened;
        if (candidates.Count > 1)
        {
            throw new InvalidOperationException(
                "Several TE3 dialogs opened ("
                    + string.Join(", ", candidates.Select(w => $"'{w.Title}'"))
                    + "); not guessing which one the action opened."
            );
        }

        return candidates.FirstOrDefault();
    }

    /// <summary>"closed" when the window is gone, "cloaked" when it is still there but cloaked, else "open".</summary>
    public static string State(IEnumerable<Win32WindowInfo> now, nint hwnd) =>
        now.FirstOrDefault(w => w.Hwnd == hwnd) switch
        {
            null => "closed",
            { IsCloaked: true } => "cloaked",
            _ => "open",
        };
}
