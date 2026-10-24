using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FlaUI.Mcp.Core;

/// <summary>
/// Focus and close change the desktop like input does, so they take the same guards: the input lease,
/// identity and integrity checks, mutation permission, and the blocked-provider check for UIA calls.
/// </summary>
internal static class WindowMutation
{
    public static void Focus(InputTarget target, nint hwnd) =>
        Native(
            target,
            () =>
            {
                // Focusing the window already in front changes nothing, so it is not counted; restoring a
                // minimized window is a change even when Windows then refuses to activate it.
                var desktop = InputDesktop.Current;
                var before = desktop.Foreground();
                var stateBefore = desktop.WindowState(hwnd);
                desktop.Focus(hwnd);
                try
                {
                    return desktop.Foreground() != before || desktop.WindowState(hwnd) != stateBefore;
                }
                catch
                {
                    // Focus returned; a failing read-back cannot prove that nothing changed.
                    MutationGuard.RecordDispatch();
                    throw;
                }
            }
        );

    /// <summary>Post WM_CLOSE, or throw when Windows refused it. True while the window still exists.</summary>
    public static bool Close(InputTarget target, nint hwnd, string handle)
    {
        var error = 0;
        var posted = false;
        Native(
            target,
            () =>
            {
                posted = InputDesktop.Current.PostClose(hwnd);
                error = Marshal.GetLastPInvokeError();
                return posted;
            }
        );
        if (!posted)
        {
            throw new InvalidOperationException(
                $"WM_CLOSE was not posted to window {handle} (Win32 error {error}); it was not asked to close."
            );
        }

        return InputDesktop.Current.ProcessOf(hwnd) != 0;
    }

    /// <summary>
    /// Starting a process changes the desktop like input does: same lease and permission check. Counted
    /// only when a process was returned; a start that throws or returns null started nothing.
    /// </summary>
    public static Process? Start(ProcessStartInfo info)
    {
        using var lease = GuardedInput.AcquireLease();
        OperationContext.Check();
        MutationGuard.CheckPermission();
        var started = InputDesktop.Current.StartProcess(info);
        // Null means Windows started no new process (for example a shell start handed to a running one).
        if (started != null)
        {
            MutationGuard.RecordDispatch();
        }

        return started;
    }

    /// <summary>A Win32 focus or close request. Counted as input only when Windows accepted it.</summary>
    private static void Native(InputTarget target, Func<bool> mutation)
    {
        using var lease = GuardedInput.AcquireLease();
        OperationContext.Check();
        target.EnsureAlive();
        GuardedInput.RequireDrivable(target.ProcessId, InputDesktop.Current);
        OperationContext.Check();
        MutationGuard.CheckPermission();
        if (mutation())
        {
            MutationGuard.RecordDispatch();
        }
    }

    /// <summary>Refuse a UIA call into a process whose provider is still held by an earlier call.</summary>
    public static void RefuseIfBlocked(int processId, PendingInvokeTracker? pending)
    {
        if (pending != null && pending.TryGetPending(processId, out var blocked))
        {
            throw new ProviderBlockedException(blocked);
        }
    }

    /// <summary>
    /// A UIA focus or close for a window without a native handle. With a tracker it runs modal-aware, so a
    /// provider that blocks returns as pending instead of hanging the tool; the result says which.
    /// </summary>
    public static PatternCallResult Uia(
        int processId,
        string description,
        Action call,
        PendingInvokeTracker? pending,
        Action? validate = null,
        TimeSpan? gracePeriod = null,
        UiaOptions? options = null
    )
    {
        var counted = options?.Counted ?? true;
        var target = options?.Target;
        RefuseIfBlocked(processId, pending);
        using var lease = GuardedInput.AcquireLease();
        var gate = new SendGate(description);
        void Validate()
        {
            validate?.Invoke();
            if (target != null)
            {
                target.EnsureAlive();
                GuardedInput.RequireForeground(target);
            }

            GuardedInput.RequireDrivable(processId, InputDesktop.Current);
        }

        // The gate is entered after every check (validation, integrity, permission) and right before the count
        // and the call, so a call held anywhere in its checks when the tool gives up can never go out.
        void Guarded() =>
            MutationGuard.ExecuteUncounted(
                Validate,
                () =>
                {
                    gate.Enter();
                    if (counted)
                    {
                        MutationGuard.RecordDispatch();
                    }

                    InputDesktop.Current.CallPattern(call);
                }
            );

        if (pending == null)
        {
            Guarded();
            return new PatternCallResult(PatternCallOutcome.Completed);
        }

        var result = ModalAwareInvoker.Execute(processId, description, Guarded, pending, gracePeriod);
        // The lease is released when this returns. A call still in validation then must never go out unleased,
        // interleaved with other input: close the gate, and it is certain that nothing was or will be sent.
        return result.Outcome == PatternCallOutcome.StillPending && gate.Abandon()
            ? result with
            {
                Dispatched = false,
            }
            : result;
    }

    internal sealed record UiaOptions(bool Counted, InputTarget? Target);

    /// <summary>Lets a pattern call through to its send only while the tool that started it still waits.</summary>
    private sealed class SendGate(string description)
    {
        private const int Waiting = 0;
        private const int Sending = 1;
        private const int Abandoned = 2;
        private int state = Waiting;

        /// <summary>Called by the call just before it counts and sends; refuses once the tool gave up on it.</summary>
        public void Enter()
        {
            if (Interlocked.CompareExchange(ref state, Sending, Waiting) != Waiting)
            {
                throw new InvalidOperationException(
                    $"{description} was not sent: its tool had already returned and released the input lease."
                );
            }
        }

        /// <summary>Close the gate; true when the call had not reached its send, so it never will.</summary>
        public bool Abandon() => Interlocked.CompareExchange(ref state, Abandoned, Waiting) == Waiting;
    }
}
