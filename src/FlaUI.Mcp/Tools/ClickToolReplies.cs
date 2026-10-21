using FlaUI.Core.Definitions;
using FlaUI.Mcp.Core;

namespace FlaUI.Mcp.Tools;

/// <summary>The replies windows_click gives, kept apart from the code that clicks.</summary>
public partial class ClickTool
{
    internal static string SelectionClickOutcome(string name, bool? selected) =>
        selected switch
        {
            true => $"Selected {name} after fallback click (SelectionItem confirmed)",
            false =>
                $"Clicked {name} after Select changed nothing; selection was not observed. Inspect the current selection before continuing; do not repeat the click blindly.",
            _ =>
                $"Clicked {name} after Select changed nothing; selection could not be read. Inspect the current selection before continuing; do not repeat the click blindly.",
        };

    /// <summary>What a toggle did, from the state read before and after it.</summary>
    internal static string ToggleOutcome(string name, ToggleState? before, ToggleState? after) =>
        (before, after) switch
        {
            (null, _) or (_, null) => $"Sent toggle request to {name}; its state could not be read",
            var (b, a) when b == a => $"Toggled {name}, but it is still {b}",
            var (b, a) => $"{name}: {b} -> {a}",
        };

    // An empty reason invites a blind retry; name the exception and any dialog that seems to block input.
    internal static string ClickFailure(
        string message,
        string exceptionType,
        IReadOnlyList<Win32WindowInfo> windows,
        bool patternSent = false
    )
    {
        var reason = string.IsNullOrWhiteSpace(message) ? exceptionType : message;
        if (patternSent)
        {
            // The fallback click was refused, but the Select before it went out and is counted.
            reason = GuardedInput.NoteFocusChange(reason, "Select was sent");
        }

        var modal = windows.Any(w => !w.IsEnabled && !w.IsToolWindow)
            ? windows.FirstOrDefault(w => w.IsEnabled && !w.IsToolWindow && !w.IsCloaked && w.Title.Length > 0)
            : null;
        return modal == null
            ? reason
            : $"{reason.TrimEnd('.')}. Window \"{modal.Title}\" appears to be a modal dialog blocking input; handle it first (see windows_list_windows).";
    }

    internal static string? NewWindowTitle(IEnumerable<Win32WindowInfo> before, IEnumerable<Win32WindowInfo> after)
    {
        var known = before.Select(w => w.Hwnd).ToHashSet();
        return after.FirstOrDefault(w => !known.Contains(w.Hwnd) && w.Title.Length > 0)?.Title;
    }
}
