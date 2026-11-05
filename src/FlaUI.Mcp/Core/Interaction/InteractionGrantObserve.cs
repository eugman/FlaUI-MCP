namespace FlaUI.Mcp.Core;

/// <summary>How the grant watches the user's input and the foreground, kept apart from its lifecycle.</summary>
public sealed partial class InteractionGrant
{
    /// <summary>Check the user's input and the foreground now; a safety event revokes the grant.</summary>
    public void Observe()
    {
        CancellationTokenSource? cancel = null,
            cancelSafety = null;
        lock (sync)
        {
            if (Batch == null)
            {
                cancelSafety = WatchAfterEnd();
            }
            else if (SafetyProblem() is { } revoke)
            {
                cancel = RevokeLocked(revoke.Reason, revoke.Code, safetyEvent: true, revoke.Cause, revoke.Fault);
                cancelSafety = safety;
            }
        }

        Cancel(cancel);
        Cancel(cancelSafety);
    }

    /// <summary>After a normal end, the user's input still stops safety consumers (a recording).</summary>
    private CancellationTokenSource? WatchAfterEnd()
    {
        if (!watching || safety.IsCancellationRequested || !UserInputSinceApproval(out var fault))
        {
            return null;
        }

        CaptureInputEvidence();
        (Reason, code, watching) = (UserInput + InputEvidenceSuffix(), FocusRevoked, false);
        // The batch had already ended normally: this stopped only safety consumers, so it is not a revocation.
        Record(code, Reason, GrantRevocation.AfterEnd, fault);
        monitor?.Dispose();
        monitor = null;
        return safety;
    }

    /// <summary>Why the grant must end now, or null: the user's input, a lost foreground, a failed observation.</summary>
    private Revocation? SafetyProblem()
    {
        if (promptUntil is { } until)
        {
            // While the user is asked to click, their input is expected: Rebaseline judges it. Bounded.
            return clock() > until
                ? new(
                    "the user did not click the window before the prompt ended",
                    RefusalCodes.ActivationDenied,
                    GrantRevocation.Activation
                )
                : null;
        }

        try
        {
            if (InputSequence() != inputAtApproval)
            {
                return new(UserInput, FocusRevoked, GrantRevocation.UserInput);
            }

            var current = foreground();
            if (current.Window != 0)
            {
                missingForegroundSince = null;
                Reason =
                    targets.Count == 0 || targets.Contains(current.Pid)
                        ? "Approved"
                        : "Approved; owned target activation required before input";
                return null;
            }

            missingForegroundSince ??= clock();
            return clock() - missingForegroundSince.Value > MissingForegroundGraceMilliseconds
                ? new(
                    $"Foreground unavailable for more than {MissingForegroundGraceMilliseconds} ms",
                    FocusRevoked,
                    GrantRevocation.Foreground
                )
                : null;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // An observer that stopped, or a failed foreground read, cannot vouch that the user is away.
            return new("Focus/input observation failed", FocusRevoked, GrantRevocation.ObserverFailed, FaultOf(error));
        }
    }

    private readonly record struct Revocation(string Reason, string Code, string Cause, ObserverFault? Fault = null);

    private static ObserverFault FaultOf(Exception error) =>
        error is ObserverUnavailableException unavailable ? unavailable.Fault : new(ObserverFault.Other);

    private bool UserInputSinceApproval(out ObserverFault? fault)
    {
        fault = null;
        try
        {
            return InputSequence() != inputAtApproval;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // Any observer failure (not only "unavailable") revokes rather than killing the host's monitor.
            fault = FaultOf(error);
            return true;
        }
    }

    private CancellationTokenSource RevokeLocked(
        string reason,
        string revokeCode,
        bool safetyEvent,
        string cause,
        ObserverFault? fault = null
    )
    {
        if (reason == UserInput)
        {
            CaptureInputEvidence();
            reason += InputEvidenceSuffix();
        }
        else if (cause == GrantRevocation.ObserverFailed)
        {
            // The event that coincided with the fault, if any; the reason text stays as hosts match it.
            CaptureInputEvidence();
        }

        // An end with no batch active (the grant was already revoked) keeps the evidence of how it really ended.
        if (Batch != null)
        {
            Record(revokeCode, reason, cause, fault);
        }

        (Batch, Reason, code) = (null, reason, revokeCode);
        ReleaseDisplay();
        if (safetyEvent)
        {
            watching = false;
            monitor?.Dispose();
            monitor = null;
        }

        targets.Clear();
        return lifetime;
    }

    /// <summary>An observer that can no longer vouch for the user's input is replaced, never reused.</summary>
    private static IInputObserver? Live(IInputObserver? current)
    {
        if (current == null)
        {
            return null;
        }

        try
        {
            _ = current.Sequence;
            return current;
        }
        catch (InvalidOperationException)
        {
            current.Dispose();
            return null;
        }
    }

    private long InputSequence() => physicalInput?.Invoke() ?? inputObserver?.Sequence ?? 0;

    // The last event does not need a live observer, so a fault still records the event that coincided with it.
    private void CaptureInputEvidence()
    {
        InputEventInfo? last;
        try
        {
            last = (inputObserver as IInputEventSource)?.LastEvent;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            last = null;
        }

        try
        {
            revokedInputSequence = InputSequence();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            revokedInputSequence = null;
        }

        lastInputAtRevoke = Attributed(last) is { } attributed ? Sanitized(attributed) : null;
    }

    // With the sequence unreadable (a fault), any event after approval is the one that coincided with it.
    private InputEventInfo? Attributed(InputEventInfo? last)
    {
        if (revokedInputSequence is { } revoked)
        {
            return last?.Sequence == revoked ? last : null;
        }

        return last?.Sequence > inputAtApproval ? last : null;
    }

    private static InputEventInfo Sanitized(InputEventInfo last)
    {
        var kind = last.Kind is "keyboard" or "mouse" ? last.Kind : "unknown";
        var mouse = kind == "mouse" && last.MouseKind is "move" or "button" or "wheel" ? last.MouseKind : null;
        return last with { Kind = kind, MouseKind = mouse };
    }

    private void Record(string recordCode, string reason, string cause, ObserverFault? fault)
    {
        lastRevocation = new(DateTimeOffset.UtcNow, recordCode, reason, cause, inputAtApproval, revokedInputSequence)
        {
            InputKind = lastInputAtRevoke?.Kind,
            MouseKind = lastInputAtRevoke?.MouseKind,
            Fault = fault,
            ToleratedMoves = ToleratedMoves() - toleratedAtApproval,
        };
        revokedObserver = inputObserver;
    }

    private long ToleratedMoves()
    {
        try
        {
            return (inputObserver as IInputEventSource)?.ToleratedMoves ?? 0;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            return toleratedAtApproval;
        }
    }

    // A fault latched before the next beat arrived: the same observer may since have measured that beat's gap.
    private GrantRevocation? WithLaterFault(GrantRevocation? revocation)
    {
        if (revocation?.Fault is not { GapAtNextBeatMs: null } fault || revokedObserver is not IInputEventSource source)
        {
            return revocation;
        }

        try
        {
            return source.Fault is { GapAtNextBeatMs: not null } later && later.Kind == fault.Kind
                ? revocation with
                {
                    Fault = later,
                }
                : revocation;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            return revocation;
        }
    }

    private string InputEvidenceSuffix()
    {
        if (revokedInputSequence is not { } revoked)
        {
            return string.Empty;
        }

        var eventText = lastInputAtRevoke is { } last
            ? $"; last observed {last.Kind} event {last.Utc:O}"
            : string.Empty;
        return $" (approved sequence {inputAtApproval}, revocation snapshot sequence {revoked}{eventText})";
    }
}
