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
}
