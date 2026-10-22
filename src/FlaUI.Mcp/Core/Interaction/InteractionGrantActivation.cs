using System.Drawing;

namespace FlaUI.Mcp.Core;

/// <summary>
/// The prompt flow: Windows refused to bring the host's window forward, so the host asks the user to click it. The
/// grant holds its decision while the user is asked, then continues only if a click, and nothing else, landed in
/// the host's window. The grant keeps the prompt's starting counts itself, so a host cannot move the baseline back.
/// </summary>
public sealed partial class InteractionGrant
{
    public static readonly TimeSpan DefaultPromptWindow = TimeSpan.FromSeconds(120);

    private long? promptUntil;
    private InputCounts promptCounts;

    /// <summary>
    /// Start asking the user to click the host's window: input and launches are refused, and the user's input does
    /// not revoke the grant, until <see cref="Rebaseline"/> judges it or <paramref name="window"/> (120 s by default)
    /// runs out, which revokes it with activation-denied. Refused while a prompt is already open.
    /// </summary>
    public void BeginPrompt(TimeSpan? window = null)
    {
        lock (sync)
        {
            if (Batch == null)
            {
                throw Refused(code, "Interaction approval required: " + Reason);
            }

            if (promptUntil != null)
            {
                throw Refused(ActivationPending, "A click prompt is already open; judge it with Rebaseline first.");
            }

            if ((counts?.Invoke() ?? inputObserver?.Counts) is not { } mark)
            {
                throw Refused(
                    RefusalCodes.ActivationDenied,
                    "No input counts are available, so a click cannot be told from typing."
                );
            }

            promptCounts = mark;
            promptUntil = clock() + (long)(window ?? DefaultPromptWindow).TotalMilliseconds;
        }
    }

    /// <summary>
    /// Call at the first foreground poll (every 250 ms) that sees the host's window, <paramref name="bounds"/>, in
    /// front after <see cref="BeginPrompt"/>. Keeps the batch, counting input from now on, only when since the prompt
    /// began there was no keyboard input, at least one button-down, and the last one inside the window; otherwise,
    /// or once the prompt has expired, revokes it with activation-denied. Decided atomically under the grant's lock;
    /// refused when no prompt is open; a revoked grant stays revoked until a new batch.
    /// </summary>
    public void Rebaseline(Rectangle bounds)
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

            if (promptUntil is not { } until)
            {
                throw Refused(RefusalCodes.ActivationDenied, "No click prompt is open; call BeginPrompt first.");
            }

            problem =
                clock() > until
                    ? "the user did not click the window before the prompt ended"
                    : ActivationProblem(promptCounts, bounds);
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
