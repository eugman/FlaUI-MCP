using System.Drawing;
using FlaUI.Mcp.Core;

/// <summary>
/// The user's consent for the TE3 tools that take the desktop, on Core's <see cref="InteractionGrant"/>: an
/// in-memory grant for one described batch that the user's own keyboard or mouse input revokes. TE3 adds a grant
/// that never approves (the tools built without one) and keeps its own names for the grant's refusal codes.
/// </summary>
public sealed class Te3Interaction : IDisposable
{
    /// <summary>The refusal code when no batch was approved, or it ended.</summary>
    public const string ApprovalRequired = InteractionGrant.ApprovalRequired;

    /// <summary>The refusal code once the user's own input revoked the batch.</summary>
    public const string FocusRevoked = InteractionGrant.FocusRevoked;

    /// <summary>The refusal code while an earlier call's failure after input has not been looked at.</summary>
    public const string PausedAfterFailure = InteractionGrant.PausedAfterFailure;

    /// <summary>The refusal code when the run's TE3 came to the front by anything but the user's click on it.</summary>
    public const string ActivationDenied = RefusalCodes.ActivationDenied;

    // Null for a grant that never approves: the tools built without one refuse input and declare nothing.
    private readonly InteractionGrant? grant;

    /// <param name="physicalInput">
    /// Counts non-injected input; null hooks the real keyboard and mouse once a batch begins. An injected count
    /// scripts the desktop too, so the foreground then always reads as available.
    /// </param>
    public Te3Interaction(Func<long>? physicalInput = null) =>
        grant = physicalInput == null ? new InteractionGrant() : new(Scripted, physicalInput);

    /// <param name="createObserver">Starts an observer once a batch begins (scripted foreground, as above).</param>
    internal Te3Interaction(Func<IInputObserver> createObserver) =>
        grant = new InteractionGrant(Scripted, createObserver: createObserver);

    private Te3Interaction(InteractionGrant? grant) => this.grant = grant;

    /// <summary>A grant that never approves, for a tool built without one.</summary>
    public static Te3Interaction Refusing() => new((InteractionGrant?)null);

    private static (nint Window, int Pid) Scripted() => (1, Environment.ProcessId);

    public bool Active => grant?.Active ?? false;

    public void Begin(string description, bool userApproved) =>
        (grant ?? throw new InvalidOperationException("This tool was built without a grant.")).Begin(
            description,
            userApproved
        );

    public void End(string why = "Batch finished") => grant?.End(why);

    /// <summary>
    /// Throws Core's typed refusal unless a batch is approved, unpaused, and the user has not touched the machine
    /// since; the message adds which TE3 tool lifts it.
    /// </summary>
    public void Require()
    {
        if (grant == null)
        {
            throw InputRefusedException.Record(
                new InputRefusal(ApprovalRequired, "This tool was built without a grant; no input sent.")
                {
                    ActionDispatched = false,
                }
            );
        }

        try
        {
            grant.Require();
        }
        catch (InputRefusedException refusal)
        {
            throw InputRefusedException.Record(
                refusal.Refusal with
                {
                    Message = refusal.Message.TrimEnd('.') + "." + Hint(refusal.Code),
                }
            );
        }
    }

    private static string Hint(string code) =>
        code == PausedAfterFailure
            ? " Observe TE3 with te3_layout_read list or te3_model_state, then retry."
            : $" Ask the user, then call {Te3InteractionTool.Begin}.";

    /// <summary>Requires the grant now and before every input the core tools send, until disposed.</summary>
    public IDisposable Guard()
    {
        Require();
        var previous = MutationGuard.BeforeMutation.Value;
        MutationGuard.BeforeMutation.Value = Require;
        return new Restore(previous);
    }

    private sealed class Restore(Action? previous) : IDisposable
    {
        public void Dispose() => MutationGuard.BeforeMutation.Value = previous;
    }

    /// <summary>Refuses input until <see cref="Observed"/>: a call failed after sending input.</summary>
    internal void Pause(string why) => grant?.Pause(why);

    /// <summary>A read of TE3 succeeded, so its state after the failure has been seen; input may resume.</summary>
    internal void Observed() => grant?.Observed();

    /// <summary>
    /// The run asks the user to click its TE3 (Windows refused to focus it): Core's grant holds input and keeps the
    /// prompt's counts until <see cref="Rebaseline"/> judges the click.
    /// </summary>
    internal void BeginPrompt() =>
        (grant ?? throw new InvalidOperationException("This tool was built without a grant.")).BeginPrompt();

    /// <summary>
    /// At the first foreground poll (every 250 ms) that sees TE3 in front: continues only when, since the prompt, a
    /// click and no typing came and the last click was inside <paramref name="bounds"/>; otherwise revokes with
    /// activation-denied. See <see cref="InteractionGrant.Rebaseline"/>.
    /// </summary>
    internal void Rebaseline(Rectangle bounds) =>
        (grant ?? throw new InvalidOperationException("This tool was built without a grant.")).Rebaseline(bounds);

    /// <summary>Ends an open click prompt that cannot be judged: later input is refused with activation-denied.</summary>
    internal void DenyActivation(string why) =>
        grant?.End("Activation not accepted: " + why, safetyEvent: true, code: ActivationDenied);

    public void Dispose() => grant?.Dispose();
}
