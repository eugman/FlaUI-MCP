using FlaUI.Mcp;
using FlaUI.Mcp.Core;

/// <summary>
/// A Core tool call that failed or is still pending, with Core's outcome for it: one of Core's refusals
/// (<see cref="AutomationRefusal"/>), so a poll that catches InvalidOperationException cannot swallow it. The message
/// is the tool's text, for people; code reads <see cref="Outcome"/> (dispatch, inputsSent, failure) or the
/// refusal's Code, InputsSent and Disposition instead.
/// </summary>
public sealed class CoreCallException : AutomationRefusal
{
    /// <summary>The code of a failed call whose outcome names no refusal.</summary>
    public const string ToolFailed = "tool-failed";

    public CoreCallException(string? tool, McpToolResult result)
        : this(tool, result, result.Outcome ?? new ToolOutcome("unknown", "unknown")) { }

    private CoreCallException(string? tool, McpToolResult result, ToolOutcome outcome)
        : base(outcome.Failure?.Code ?? ToolFailed, string.Join("; ", result.Content.Select(c => c.Text)))
    {
        Tool = tool;
        Outcome = outcome;
        InputsSent = outcome.InputsSent ?? 0;
        ActionDispatched =
            InputsSent > 0 || outcome.Failure?.ActionDispatched == true || outcome.Dispatch is "partial" or "completed";
    }

    public string? Tool { get; }

    /// <summary>Core's outcome. Core's registry always sets one; a result without it reads as unknown.</summary>
    public ToolOutcome Outcome { get; }

    /// <summary>Uncertain while the failing call may still reach TE3 (timed out pending, or its provider held).</summary>
    public override RefusalDisposition Disposition =>
        CoreFailure.MayStillGoOut(Outcome) ? RefusalDisposition.Uncertain : base.Disposition;
}

/// <summary>What a failure's structured outcome says about input and the desktop, read instead of its text.</summary>
internal static class CoreFailure
{
    // Refusals that retrying cannot get past: Windows kept another window in front, there is no desktop, or the
    // user has not approved (or has revoked) the batch.
    private static readonly HashSet<string> DesktopBlockers =
    [
        RefusalCodes.ActivationDenied,
        RefusalCodes.DesktopUnavailable,
        Te3Interaction.ApprovalRequired,
        Te3Interaction.FocusRevoked,
        Te3Interaction.PausedAfterFailure,
        RefusalCodes.FocusUnavailable,
        // The target's window or process is gone, never was, or could not be started: a retry meets the same.
        RefusalCodes.TargetStale,
        RefusalCodes.WindowNotFound,
        RefusalCodes.LaunchFailed,
    ];

    /// <summary>
    /// Whether a refusal says the window itself is gone (its process exited or restarted, it closed, or it cannot be
    /// tied to its process any more): a window that closed meanwhile, as opposed to a guard refusing input.
    /// </summary>
    internal static bool IsGone(Exception error) =>
        error
            is AutomationRefusal
            {
                Code: RefusalCodes.TargetStale or RefusalCodes.WindowOwnershipUnknown or RefusalCodes.WindowNotFound,
            };

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
                case AutomationRefusal other:
                    return new ToolOutcome(
                        other.Disposition == RefusalDisposition.NoInput ? "not-dispatched" : "partial",
                        "not-tracked"
                    )
                    {
                        Failure = new InputRefusal(other.Code, other.Message)
                        {
                            ActionDispatched = other.ActionDispatched,
                        },
                    };
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
    /// <param name="walk">An object navigation's walk account, carried as the outcome's top-level <c>walk</c>.</param>
    internal static ToolOutcome Reply(Exception error, int inputsSent, object? walk = null)
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
            Details = WalkAccount.Details(walk, null),
        };
    }

    /// <summary>
    /// The desktop condition that refused the input, when retrying cannot help (activation-denied,
    /// desktop-unavailable, the grant's codes), hyphenated; an underscored spelling (activation_denied) is accepted.
    /// </summary>
    internal static string? Blocker(Exception error) =>
        Outcome(error)?.Failure?.Code is { } code && DesktopBlockers.Contains(RefusalCodes.Canonical(code))
            ? RefusalCodes.Canonical(code)
            : null;
}
