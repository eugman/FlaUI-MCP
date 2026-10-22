using FlaUI.Mcp.Core;

/// <summary>
/// TE3 prompts that can block a run, with what to do about each. The titles come from TE3's documentation and release
/// notes and have not been observed by this host (Unverified): any window that matches is refused before input, and
/// the refusal says how to clear it. Nothing is ever answered here.
/// </summary>
internal static class Te3Dialogs
{
    private static readonly (string Match, string Advice)[] Known =
    [
        (
            "Get Started",
            "TE3's Get Started page, shown on first launch and after an upgrade. Close it by hand; registry policies "
                + @"(Software\Policies\Tabular Editor ApS\TE3) can stop the network calls behind it"
        ),
        ("External changes detected", "TE3 found the model changed on disk; Reload or Ignore is the user's decision"),
        (
            "workspace",
            "TE3 asks for a workspace database because the model's .tmuo has no UseWorkspace; answer it, or pre-seed "
                + "the .tmuo (UseWorkspace, WorkspaceConnection, WorkspaceDatabase)"
        ),
    ];

    /// <summary>What to do about a known prompt with this title, or null.</summary>
    /// <summary>Whether a window titled <paramref name="title"/> opened since <paramref name="before"/> was taken.</summary>
    public static bool Opened(WindowBaseline before, IEnumerable<Win32WindowInfo> now, string title) =>
        before.Opened(now).Any(w => w.Title == title);

    public static string? Advice(string title) =>
        Known.FirstOrDefault(k => title.Contains(k.Match, StringComparison.OrdinalIgnoreCase)).Advice;

    /// <summary>The window's title, with the advice when it is a known prompt.</summary>
    public static string Describe(Win32WindowInfo window) =>
        Advice(window.Title) is { } advice ? $"\"{window.Title}\" ({advice})" : $"\"{window.Title}\"";

    // A floating pane (Dependencies) is not a dialog; a known prompt is named with what to do about it.
    public static void RequireNoOtherWindow(
        IReadOnlyList<Win32WindowInfo> windows,
        nint main,
        string allowedDialog,
        Func<nint, WindowShape?>? shapeOf = null
    )
    {
        shapeOf ??= WindowShape.Read;
        var unexpected = windows
            .Where(w => w.Hwnd != main && w.Title.Length > 0 && w.Title != allowedDialog)
            .Where(w => !LayoutChecks.IsFloatingPane(w, shapeOf) || Advice(w.Title) != null)
            .ToArray();
        if (unexpected.Length > 0)
        {
            throw new InvalidOperationException(
                "An additional TE3 window is open: "
                    + string.Join(
                        "; ",
                        unexpected.Select(w =>
                            $"{Te3Dialogs.Describe(w)} (HWND {w.Hwnd}, tool window: {w.IsToolWindow})"
                        )
                    )
                    + ". Inspect it; no dialog was dismissed."
            );
        }
    }
}
