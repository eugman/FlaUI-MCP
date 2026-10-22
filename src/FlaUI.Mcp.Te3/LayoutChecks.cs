using System.Drawing;
using FlaUI.Mcp.Core;
using FlaUI.Mcp.Te3;

/// <summary>
/// The checks that decide whether a te3_layout change happened, kept apart from the UIA reads so they can be tested:
/// a change that cannot show its effect is a failure, not a note.
/// </summary>
internal static class LayoutChecks
{
    // A pane within this many pixels of its requested size has reached it.
    public const int SizeTolerance = 4;

    /// <summary>One resize drag and what it measured; bounds are window-relative.</summary>
    public sealed record ResizeMeasure(
        string Title,
        string Edge,
        Point From,
        Point To,
        object Before,
        object After,
        int Requested,
        int Reached,
        int InputsSent
    );

    /// <summary>
    /// The window's origin read before and after its pane bounds must agree: a drag is planned in raw window pixels
    /// from one snapshot, and a window that moved between the reads would shift it.
    /// </summary>
    public static void RequireStill(Rectangle before, Rectangle after)
    {
        if (before != after)
        {
            throw new InvalidOperationException(
                $"The TE3 window moved from {before} to {after} while its panes were measured; no drag sent. Retry."
            );
        }
    }

    /// <summary>Whether a pane is already within the tolerance of its requested size, so no drag is needed.</summary>
    public static bool AlreadyThere(int requested, int current) => Math.Abs(current - requested) <= SizeTolerance;

    /// <summary>The step for a pane left alone because it was already at its size: no input.</summary>
    public static object Skipped(string title, int requested, int reached) =>
        new
        {
            title,
            requested,
            reached,
            inputsSent = 0,
            skipped = true,
        };

    /// <summary>The step a resize reports; throws, with the step, when the pane missed its size.</summary>
    public static object Measured(ResizeMeasure measure)
    {
        var step = new
        {
            title = measure.Title,
            edge = measure.Edge,
            from = new { x = measure.From.X, y = measure.From.Y },
            to = new { x = measure.To.X, y = measure.To.Y },
            before = measure.Before,
            after = measure.After,
            requested = measure.Requested,
            reached = measure.Reached,
            attempts = SplitterPlan.MaxAttempts,
            inputsSent = measure.InputsSent,
        };
        if (Math.Abs(measure.Reached - measure.Requested) > SizeTolerance)
        {
            throw new LayoutSteps.StepFailedException(
                SplitterPlan.Missed(
                    measure.Title,
                    measure.Edge,
                    measure.From,
                    measure.To,
                    measure.Requested,
                    measure.Reached,
                    SplitterPlan.MaxAttempts
                ),
                step
            );
        }

        return step;
    }

    /// <summary>
    /// keep closes every pane it is not told to keep, so a title that names no visible pane or tab (a typo) is refused
    /// before the first click instead of closing everything.
    /// </summary>
    public static void RequireKept(IEnumerable<string> titles, IEnumerable<string> visible)
    {
        var shown = visible.ToList();
        var unknown = titles.Where(t => !shown.Any(v => Te3Layout.TitleMatches(v, t))).ToList();
        if (unknown.Count > 0)
        {
            throw new ArgumentException(
                $"keep names {string.Join(", ", unknown.Select(t => $"'{t}'"))}, which no visible pane or tab has "
                    + $"(visible: {string.Join(", ", shown.Select(t => $"'{t}'"))}). Nothing was closed; no input sent."
            );
        }
    }

    private const int ReadBackMs = 1000;
    private const int PollMs = 100;

    /// <summary>
    /// After pressing a pane's Maximize or Restore: waits up to a second for <paramref name="flipped"/> (the title bar
    /// now shows the other button), and throws when it never does, since the press may have been lost.
    /// </summary>
    public static async Task ConfirmPaneButton(string pane, string pressed, Func<bool> flipped, Func<int, Task> delay)
    {
        for (var waited = 0; !flipped(); waited += PollMs)
        {
            if (waited >= ReadBackMs)
            {
                throw new InvalidOperationException(
                    $"Pressed {pressed} on '{pane}', but its title bar still shows {pressed} after {ReadBackMs} ms: the "
                        + "press may have been lost. Read the layout with te3_layout_read list before retrying."
                );
            }

            await delay(PollMs);
        }
    }

    /// <summary>
    /// The titled, non-tool window of the TE3 process that a click opened: one that was not open in
    /// <paramref name="before"/>, read before the click. Null when there is none.
    /// </summary>
    public static Win32WindowInfo? NewDialog(
        IEnumerable<Win32WindowInfo> windows,
        nint main,
        IReadOnlyCollection<nint> before
    ) =>
        windows.FirstOrDefault(w =>
            w.Hwnd != main && !before.Contains(w.Hwnd) && w.Title.Length > 0 && !w.IsToolWindow
        );

    /// <summary>
    /// The TE3 windows when a layout call starts, which later checks compare with. Refuses, before any input, a titled
    /// window already open that is not the main window or a floating pane: a dialog nobody has answered.
    /// </summary>
    /// <param name="shapeOf">A window's class and owner, or null when it cannot be read.</param>
    public static IReadOnlyCollection<nint> Baseline(
        IReadOnlyList<Win32WindowInfo> windows,
        nint main,
        Func<nint, WindowShape?> shapeOf
    )
    {
        if (
            windows.FirstOrDefault(w => w.Hwnd != main && w.Title.Length > 0 && !IsFloatingPane(w, shapeOf)) is { } open
        )
        {
            throw new InvalidOperationException(
                $"TE3 already shows {Te3Dialogs.Describe(open)}. Answer or close it, then retry; no input sent."
            );
        }

        return windows.Select(w => w.Hwnd).ToList();
    }

    // Floating dock panes observed on 3.27.1. A window is one only when its title matches, it has no owner, and its
    // class is the WinForms window class the floating Dependencies pane showed; TE3's dialogs are owned by the main
    // window, so a dialog titled like a pane is never waved through. The pane's AutomationId (DaxDependencies) is on
    // a child, not on the top-level window, so it is not used.
    private static readonly string[] FloatingPanes = ["Dependencies"];

    private const string FloatingClass = "WindowsForms10.Window.8.";

    internal static bool IsFloatingPane(Win32WindowInfo window, Func<nint, WindowShape?> shapeOf) =>
        FloatingPanes.Any(title => Te3Layout.TitleMatches(window.Title, title))
        && shapeOf(window.Hwnd) is { Owner: 0 } shape
        && shape.ClassName.StartsWith(FloatingClass, StringComparison.Ordinal);
}
