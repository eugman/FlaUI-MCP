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
                cancel = RevokeLocked(revoke.Reason, revoke.Code, safetyEvent: true);
                cancelSafety = safety;
            }
        }

        Cancel(cancel);
        Cancel(cancelSafety);
    }

    /// <summary>After a normal end, the user's input still stops safety consumers (a recording).</summary>
    private CancellationTokenSource? WatchAfterEnd()
    {
        if (!watching || safety.IsCancellationRequested || !UserInputSinceApproval())
        {
            return null;
        }

        CaptureInputEvidence();
        (Reason, code, watching) = (UserInput + InputEvidenceSuffix(), FocusRevoked, false);
        monitor?.Dispose();
        monitor = null;
        return safety;
    }

    /// <summary>Why the grant must end now, or null: the user's input, a lost foreground, a failed observation.</summary>
    private (string Reason, string Code)? SafetyProblem()
    {
        if (promptUntil is { } until)
        {
            // While the user is asked to click, their input is expected: Rebaseline judges it. Bounded.
            return clock() > until
                ? ("the user did not click the window before the prompt ended", RefusalCodes.ActivationDenied)
                : null;
        }

        try
        {
            if (InputSequence() != inputAtApproval)
            {
                return (UserInput, FocusRevoked);
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
                ? ($"Foreground unavailable for more than {MissingForegroundGraceMilliseconds} ms", FocusRevoked)
                : null;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // An observer that stopped, or a failed foreground read, cannot vouch that the user is away.
            return ("Focus/input observation failed", FocusRevoked);
        }
    }

    private bool UserInputSinceApproval()
    {
        try
        {
            return InputSequence() != inputAtApproval;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // Any observer failure (not only "unavailable") revokes rather than killing the host's monitor.
            return true;
        }
    }

    private CancellationTokenSource RevokeLocked(string reason, string revokeCode, bool safetyEvent)
    {
        if (reason == UserInput)
        {
            CaptureInputEvidence();
            reason += InputEvidenceSuffix();
        }

        (Batch, Reason, code) = (null, reason, revokeCode);
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

    private void CaptureInputEvidence()
    {
        try
        {
            revokedInputSequence = InputSequence();
            var last = (inputObserver as IInputEventSource)?.LastEvent;
            lastInputAtRevoke = last?.Sequence == revokedInputSequence ? last : null;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            revokedInputSequence = null;
            lastInputAtRevoke = null;
        }
    }

    private string InputEvidenceSuffix()
    {
        if (revokedInputSequence is not { } revoked)
        {
            return string.Empty;
        }

        var eventText = lastInputAtRevoke is { } last ? $"; last {last.Kind} event {last.Utc:O}" : string.Empty;
        return $" (approved sequence {inputAtApproval}, revoked sequence {revoked}{eventText})";
    }
}
