namespace FlaUI.Mcp.Core;

/// <summary>
/// The top-level windows of a process before an action, to judge what the action opened or blocked against.
/// Only new windows count as opened, and never a tooltip, menu, tool window or cloaked window. A modal is a window
/// owned by one that is disabled: the main window is never blamed just because some other window is disabled.
/// </summary>
public sealed class WindowBaseline(IEnumerable<Win32WindowInfo> before)
{
    private readonly Dictionary<nint, Win32WindowInfo> before = before
        .GroupBy(window => window.Hwnd)
        .ToDictionary(group => group.Key, group => group.First());

    /// <summary>A baseline with no windows: every window counts as new.</summary>
    public static WindowBaseline Empty { get; } = new([]);

    /// <summary>The process's windows now, through the desktop seam; empty for process 0.</summary>
    public static WindowBaseline Take(int processId) =>
        processId == 0 ? Empty : new(InputDesktop.Current.TopLevelWindows(processId));

    /// <summary>Whether a window can be a dialog: not a tooltip or menu, not a tool window, not cloaked.</summary>
    public static bool CanBeDialog(Win32WindowInfo window) =>
        !window.IsToolWindow && !window.IsCloaked && window.ClassName is not ("tooltips_class32" or "#32768");

    /// <summary>The windows in <paramref name="after"/> that are new and can be dialogs, owned and titled first.</summary>
    public IReadOnlyList<Win32WindowInfo> Opened(IEnumerable<Win32WindowInfo> after) =>
        after
            .Where(window => !before.ContainsKey(window.Hwnd) && CanBeDialog(window))
            .OrderByDescending(window => window.Owner != 0)
            .ThenByDescending(window => window.Title.Length > 0)
            .ToList();

    /// <summary>Whether a window that was enabled is now disabled, as a modal dialog disables its owner.</summary>
    public bool OwnerDisabled(IEnumerable<Win32WindowInfo> after) =>
        after.Any(window => before.TryGetValue(window.Hwnd, out var was) && was.IsEnabled && !window.IsEnabled);

    /// <summary>
    /// The title of a modal the action opened: null unless an owner became disabled; then the new dialog's
    /// title, "(untitled window)", or "(unknown title)" when no new dialog is listed.
    /// </summary>
    public string? NewModal(IReadOnlyList<Win32WindowInfo> after)
    {
        if (!OwnerDisabled(after))
        {
            return null;
        }

        if (Opened(after).FirstOrDefault() is not { } dialog)
        {
            return "(unknown title)";
        }

        return dialog.Title.Length > 0 ? dialog.Title : "(untitled window)";
    }

    /// <summary>
    /// A titled, enabled dialog in <paramref name="windows"/> owned by a disabled window of the list: the modal
    /// that blocks input now, whenever it opened. Null when none is.
    /// </summary>
    public static Win32WindowInfo? BlockingDialog(IReadOnlyList<Win32WindowInfo> windows)
    {
        var disabled = windows.Where(window => !window.IsEnabled).Select(window => window.Hwnd).ToHashSet();
        return windows.FirstOrDefault(window =>
            window.IsEnabled && window.Title.Length > 0 && CanBeDialog(window) && disabled.Contains(window.Owner)
        );
    }
}
