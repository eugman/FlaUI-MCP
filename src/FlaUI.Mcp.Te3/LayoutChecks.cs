using System.Drawing;

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
}
