using System.Runtime.ExceptionServices;

namespace FlaUI.Mcp.Core;

/// <summary>
/// Outcome of a modal-aware pattern call.
/// </summary>
public enum PatternCallOutcome
{
    /// <summary>The pattern call returned within the grace period.</summary>
    Completed,

    /// <summary>A modal dialog opened; the pattern call is still executing in the background.</summary>
    ModalDetected,

    /// <summary>The pattern call did not return within the grace period and no modal was detected.</summary>
    StillPending,
}

/// <summary>
/// Result of a modal-aware pattern call.
/// </summary>
/// <param name="Outcome">What happened within the grace period.</param>
/// <param name="ModalTitle">Title of the detected modal window, if any.</param>
/// <param name="Dispatched">
/// False while a still-pending call has not counted its dispatch. Callers of <see cref="ModalAwareInvoker.Execute"/>
/// must count through <see cref="MutationGuard.Execute"/> or <see cref="MutationGuard.RecordDispatch"/>, or a
/// pending call reports timed_out_pending. Always true for a completed call or a detected modal.
/// </param>
public sealed record PatternCallResult(
    PatternCallOutcome Outcome,
    string? ModalTitle = null,
    string? PendingPatternId = null,
    bool Dispatched = true
)
{
    /// <summary>The last failed read of the app's windows while waiting, when the call is still pending.</summary>
    public string? WaitError { get; init; }

    /// <summary>The sentence a pending reply adds for <see cref="WaitError"/>, or empty.</summary>
    internal string WaitNote =>
        WaitError == null
            ? ""
            : $" While waiting, reading the app's windows failed ({WaitError}), so a modal dialog may have gone undetected.";
}

/// <summary>
/// Executes UI Automation pattern calls (Invoke, Toggle, Select, ...) so that a
/// handler which opens a modal dialog does not hang the calling tool.
///
/// Background: UIA pattern calls are synchronous cross-process calls. In WinForms
/// and many other frameworks, a button handler that calls ShowDialog() will not
/// return until the dialog closes — so the pattern call, and with it the entire
/// UIA provider of the target process, stays blocked for as long as the dialog
/// is open. This class runs the call on a background thread and watches the
/// target process's top-level windows via non-blocking Win32 APIs. When a new
/// window appears (or an existing window is disabled by a modal), it reports
/// ModalDetected right away so the caller can interact with the dialog instead
/// of timing out.
/// </summary>
public static class ModalAwareInvoker
{
    /// <summary>How long to wait for the pattern call before giving up on completion.</summary>
    public static readonly TimeSpan DefaultGracePeriod = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Execute a pattern call with modal detection.
    /// </summary>
    /// <param name="processId">Target process id (0 if unknown; disables modal detection).</param>
    /// <param name="description">Human-readable description, e.g. "Invoke on 'OK'".</param>
    /// <param name="patternCall">The synchronous pattern call to execute.</param>
    /// <param name="tracker">Tracker that other tools consult to fail fast while the call is pending.</param>
    /// <param name="gracePeriod">Max time to wait for completion; defaults to <see cref="DefaultGracePeriod"/>.</param>
    /// <param name="windowEnumerator">Override for window enumeration (for testing).</param>
    /// <exception cref="Exception">Rethrows any exception from <paramref name="patternCall"/> when it completes within the grace period.</exception>
    internal static PatternCallResult Execute(
        int processId,
        string description,
        Action patternCall,
        PendingInvokeTracker tracker,
        TimeSpan? gracePeriod = null,
        Func<int, IReadOnlyList<Win32WindowInfo>>? windowEnumerator = null
    )
    {
        OperationContext.Check();
        var grace = gracePeriod ?? DefaultGracePeriod;
        var desktop = InputDesktop.Current;
        windowEnumerator ??= desktop.TopLevelWindows;

        var baseline = Baseline(windowEnumerator, processId, description);
        var operation = OperationContext.Current.Value;
        var sentBefore = operation?.InputsSent ?? 0;
        var info = tracker.Begin(processId, description);
        var task = Task.Run(() =>
        {
            OperationContext.Check();
            patternCall();
        });
        var ending = new LateEnding(operation, description);

        // Ensure the tracker is cleared whenever the call eventually returns, even if we stop waiting for it
        // below, and keep a late failure on the operation instead of dropping it.
        _ = task.ContinueWith(
            t =>
            {
                ending.Finished(t);
                tracker.Complete(info);
            },
            TaskScheduler.Default
        );

        var waited = AwaitResult(
            task,
            processId,
            baseline,
            windowEnumerator,
            desktop.Time,
            desktop.Time.GetUtcNow() + grace,
            (int)Math.Ceiling(grace / PollInterval)
        );
        if (waited is WaitOutcome<PatternCallResult>.Done done)
        {
            return done.Value;
        }

        ending.ReportedPending(task);
        if (waited is WaitOutcome<PatternCallResult>.Changed modal)
        {
            info.ModalTitle = modal.What;
            return new PatternCallResult(PatternCallOutcome.ModalDetected, modal.What, info.OperationId);
        }

        // A pattern call must count itself through MutationGuard.Execute (or RecordDispatch) once it is sent;
        // that happens only after validation, which a hung provider can hold up. A call still pending that has
        // not counted itself is reported as not yet sent (timed_out_pending). A modal dialog proves the call
        // reached the app, and without an operation to count against the answer stays that it may have been sent.
        var pending = new PatternCallResult(PatternCallOutcome.StillPending, PendingPatternId: info.OperationId)
        {
            WaitError = (waited as WaitOutcome<PatternCallResult>.TimedOut)?.LastError?.Message,
        };
        return operation == null ? pending : pending with { Dispatched = operation.InputsSent > sentBefore };
    }

    /// <summary>
    /// The windows before the call. A failed read refuses before anything is sent: an empty baseline would turn modal
    /// detection off for the whole call without saying so.
    /// </summary>
    private static WindowBaseline Baseline(
        Func<int, IReadOnlyList<Win32WindowInfo>> windowEnumerator,
        int processId,
        string description
    )
    {
        if (processId == 0)
        {
            return WindowBaseline.Empty;
        }

        var (windows, error) = ReadWindows(windowEnumerator, processId);
        if (error != null)
        {
            throw InputRefusedException.Record(
                new InputRefusal(
                    "window-baseline-unreadable",
                    $"The app's windows could not be read before {description} ({error.Message}), so a modal dialog it opens could not be detected; no input sent."
                )
                {
                    ActionDispatched = false,
                }
            );
        }

        return new WindowBaseline(windows);
    }

    /// <summary>The process's windows, or the read error: a failed read never ends the wait.</summary>
    private static (IReadOnlyList<Win32WindowInfo> Windows, Exception? Error) ReadWindows(
        Func<int, IReadOnlyList<Win32WindowInfo>> windowEnumerator,
        int processId
    )
    {
        try
        {
            return (windowEnumerator(processId), null);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return ([], error);
        }
    }

    /// <summary>
    /// Records a pattern call's failure on its operation when the failure comes after the tool already reported
    /// the call as pending, whichever of the two happens first.
    /// </summary>
    private sealed class LateEnding(OperationContext? operation, string description)
    {
        private readonly object gate = new();
        private bool reportedPending;

        public void ReportedPending(Task task)
        {
            lock (gate)
            {
                reportedPending = true;
                Record(task);
            }
        }

        public void Finished(Task task)
        {
            _ = task.Exception;
            lock (gate)
            {
                if (reportedPending)
                {
                    Record(task);
                }
            }
        }

        private void Record(Task task)
        {
            if (operation != null && task.IsFaulted && task.Exception is { } error)
            {
                var reason = (error.InnerException ?? error).Message;
                operation.LateError = $"{description} failed after it was reported pending: {reason}";
            }
        }
    }

    /// <summary>
    /// Wait for the call until it returns (Done), a modal appears (Changed, with its title), or the deadline
    /// passes (TimedOut, with the last window-read error). A call that failed within the wait is rethrown. The
    /// polls are capped at the grace period over the poll interval as well, so a clock that never moves (a fake
    /// held still) cannot keep a hung call's wait going.
    /// </summary>
    private static WaitOutcome<PatternCallResult> AwaitResult(
        Task task,
        int processId,
        WindowBaseline baseline,
        Func<int, IReadOnlyList<Win32WindowInfo>> windowEnumerator,
        TimeProvider time,
        DateTimeOffset deadline,
        int maxPolls
    )
    {
        Exception? lastError = null;
        for (var polls = 1; ; polls++)
        {
            // WaitAny (unlike Task.Wait) does not throw for faulted tasks,
            // letting us rethrow the original exception un-wrapped below
            if (Task.WaitAny(new[] { task }, PollInterval) == 0)
            {
                OperationContext.Check();
                if (task.IsCanceled)
                {
                    throw new OperationCanceledException();
                }

                if (task.IsFaulted && task.Exception != null)
                {
                    var inner = task.Exception.InnerException ?? task.Exception;
                    ExceptionDispatchInfo.Capture(inner).Throw();
                }

                return new WaitOutcome<PatternCallResult>.Done(new PatternCallResult(PatternCallOutcome.Completed));
            }

            if (processId != 0)
            {
                var (windows, error) = ReadWindows(windowEnumerator, processId);
                lastError = error ?? lastError;
                if (error == null && baseline.NewModal(windows) is { } modalTitle)
                {
                    return new WaitOutcome<PatternCallResult>.Changed(modalTitle);
                }
            }

            if (time.GetUtcNow() >= deadline || polls >= maxPolls)
            {
                return new WaitOutcome<PatternCallResult>.TimedOut(null, lastError);
            }
        }
    }
}
