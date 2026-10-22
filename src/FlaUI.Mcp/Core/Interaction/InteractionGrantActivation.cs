using System.Drawing;

namespace FlaUI.Mcp.Core;

/// <summary>
/// The prompt flow: Windows refused to bring the host's window forward, so the host asks the user to click it. The
/// grant holds its decision while the user is asked, then continues only if a click, and nothing else, landed in
/// the host's window.
/// </summary>
public sealed partial class InteractionGrant
{
    public static readonly TimeSpan DefaultPromptWindow = TimeSpan.FromSeconds(120);

    private long? promptUntil;

    /// <summary>
    /// Start asking the user to click the host's window: input is refused, and the user's input does not revoke the
    /// grant, until <see cref="Rebaseline"/> judges it or <paramref name="window"/> (120 s by default) runs out,
    /// which revokes it with activation-denied. Returns the counts to pass to <see cref="Rebaseline"/>.
    /// </summary>
    public InputCounts BeginPrompt(TimeSpan? window = null)
    {
        lock (sync)
        {
            if (Batch == null)
            {
                throw Refused(code, "Interaction approval required: " + Reason);
            }

            if ((counts?.Invoke() ?? inputObserver?.Counts) is not { } mark)
            {
                throw Refused(
                    RefusalCodes.ActivationDenied,
                    "No input counts are available, so a click cannot be told from typing."
                );
            }

            promptUntil = clock() + (long)(window ?? DefaultPromptWindow).TotalMilliseconds;
            return mark;
        }
    }

    /// <summary>
    /// Call at the first foreground poll (every 250 ms or so) that sees the host's window in front after
    /// <see cref="BeginPrompt"/>. Keeps the batch, counting input from now on, only when since
    /// <paramref name="prompt"/> there was no keyboard input, at least one button-down, and the last one inside
    /// <paramref name="bounds"/>; otherwise revokes it with activation-denied. Decided atomically under the grant's
    /// lock; a revoked grant stays revoked until a new batch.
    /// </summary>
    public void Rebaseline(InputCounts prompt, Rectangle bounds)
    {
        CancellationTokenSource cancel,
            cancelSafety;
        string? problem;
        lock (sync)
        {
            if (Batch == null)
            {
                throw Refused(code, "Interaction approval required: " + Reason);
            }

            problem = ActivationProblem(prompt, bounds);
            promptUntil = null;
            if (problem == null)
            {
                inputAtApproval = InputSequence();
                return;
            }

            cancel = RevokeLocked(problem, RefusalCodes.ActivationDenied, safetyEvent: true);
            cancelSafety = safety;
        }

        Cancel(cancel);
        Cancel(cancelSafety);
        throw Refused(
            RefusalCodes.ActivationDenied,
            $"Activation not accepted: {problem}. Ask the user before resuming."
        );
    }

    private string? ActivationProblem(InputCounts prompt, Rectangle bounds)
    {
        InputCounts now;
        try
        {
            now = counts?.Invoke() ?? inputObserver?.Counts ?? throw new InvalidOperationException("no observer");
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            return "the input observer cannot tell a click from typing";
        }

        if (now.Keys != prompt.Keys)
        {
            return "keyboard input from the user was observed while waiting, so it went to another window";
        }

        if (now.ButtonDowns == prompt.ButtonDowns)
        {
            return "the window came to the front without a click from the user";
        }

        return now.LastButtonDown is { } point && bounds.Contains(point)
            ? null
            : "the last click was not inside the window, so something else brought it to the front";
    }
}
