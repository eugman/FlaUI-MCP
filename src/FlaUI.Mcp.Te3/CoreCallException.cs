using FlaUI.Mcp;
using FlaUI.Mcp.Core;

/// <summary>
/// A Core tool call that failed or is still pending, with Core's outcome for it. The message is the tool's text, for
/// people; code reads <see cref="Outcome"/> (dispatch, inputsSent, failure) instead.
/// </summary>
public sealed class CoreCallException(string? tool, McpToolResult result)
    : InvalidOperationException(string.Join("; ", result.Content.Select(c => c.Text)))
{
    public string? Tool => tool;

    /// <summary>Core's outcome. Core's registry always sets one; a result without it reads as unknown.</summary>
    public ToolOutcome Outcome { get; } = result.Outcome ?? new ToolOutcome("unknown", "unknown");
}

/// <summary>What a failure's structured outcome says about input and the desktop, read instead of its text.</summary>
internal static class CoreFailure
{
    // Refusals that retrying cannot get past: Windows kept another window in front, or there is no desktop.
    private static readonly HashSet<string> DesktopBlockers = ["activation-denied", "desktop-unavailable"];

    /// <summary>
    /// The outcome of the failing Core call, of a direct send that guarded input refused, or of a call refused because
    /// an earlier one still holds TE3's provider (as Core's BlockedResult), searched through the exceptions that wrap
    /// it (a retry, a cancel step).
    /// </summary>
    internal static ToolOutcome? Outcome(Exception? error)
    {
        for (; error != null; error = error.InnerException)
        {
            switch (error)
            {
                case CoreCallException call:
                    return call.Outcome;
                case InputRefusedException refused:
                    return new ToolOutcome("not-dispatched", "not-tracked") { Failure = refused.Refusal };
                case ProviderBlockedException blocked:
                    return new ToolOutcome(
                        "not-dispatched",
                        "pending",
                        blocked.Pending.OperationId,
                        blocked.Pending.ModalTitle
                    );
            }
        }

        return null;
    }

    /// <summary>
    /// Whether this failure itself, not one it wraps, is a Core call whose outcome says nothing was dispatched. What
    /// the caller's own guards changed before it (activation, focus) is in its tally, not here.
    /// </summary>
    internal static bool NotDispatched(Exception error) =>
        error is CoreCallException { Outcome.Dispatch: "not-dispatched" };

    /// <summary>
    /// Whether the failing call itself may still reach the app: it timed out pending, or it went out and its
    /// provider has not returned. A call refused because another one holds the provider never will.
    /// </summary>
    internal static bool MayStillGoOut(ToolOutcome? outcome) =>
        outcome is { Dispatch: "timed_out_pending" } or { Provider: "pending", Dispatch: not "not-dispatched" };

    /// <summary>
    /// The outcome for a TE3 tool's own failure after <paramref name="inputsSent"/> inputs: partial once any went
    /// out, otherwise not-dispatched, or timed_out_pending while the failing call may still go out. A held provider
    /// shows as provider pending, with the pending call's id and modal. It carries the refusal that stopped it, when
    /// guarded input refused.
    /// </summary>
    internal static ToolOutcome Reply(Exception error, int inputsSent)
    {
        var failing = Outcome(error);
        var dispatch =
            MayStillGoOut(failing) && inputsSent == 0
                ? "timed_out_pending"
                : ToolOutcome.DispatchFor(failed: true, inputsSent);
        return new ToolOutcome(
            dispatch,
            failing?.IsPending == true ? "pending" : "not-tracked",
            failing?.PendingPatternId,
            failing?.ModalTitle
        )
        {
            Verification = "not-established",
            Status = "failed",
            InputsSent = inputsSent,
            Failure = failing?.Failure is { } refusal ? refusal with { ActionDispatched = inputsSent > 0 } : null,
        };
    }

    /// <summary>The desktop condition that refused the input, when retrying cannot help: activation-denied or desktop-unavailable.</summary>
    internal static string? Blocker(Exception error) =>
        Outcome(error)?.Failure?.Code is { } code && DesktopBlockers.Contains(code) ? code : null;
}
