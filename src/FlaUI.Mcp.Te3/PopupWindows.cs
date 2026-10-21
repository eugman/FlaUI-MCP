using FlaUI.Mcp.Core;

/// <summary>
/// Which of TE3's top-level windows a click or key opened: those not in the baseline read just before it. A window
/// that was already open (a floating pane, the Preferences dialog) is never taken for the popup.
/// </summary>
internal static class PopupWindows
{
    /// <summary>The process's windows now, as a baseline to read before the input that opens a popup.</summary>
    public static IReadOnlyCollection<nint> Baseline(int processId) =>
        Win32Desktop.GetTopLevelWindows(processId).Select(w => w.Hwnd).ToList();

    /// <summary>The visible windows in <paramref name="now"/> that were not open in <paramref name="before"/>.</summary>
    public static Win32WindowInfo[] New(IEnumerable<Win32WindowInfo> now, IReadOnlyCollection<nint>? before) =>
        before == null
            ? throw new InvalidOperationException(
                "No window baseline was read before the popup opened, so its window cannot be told apart; no click sent."
            )
            : [.. now.Where(w => !w.IsCloaked && !before.Contains(w.Hwnd))];
}
