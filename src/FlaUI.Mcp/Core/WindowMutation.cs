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
                return desktop.Foreground() != before || desktop.WindowState(hwnd) != stateBefore;
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
    /// once Windows accepted the start; a start that throws started nothing.
    /// </summary>
    public static Process? Start(ProcessStartInfo info)
    {
        using var lease = GuardedInput.AcquireLease();
        OperationContext.Check();
        MutationGuard.CheckPermission();
        var started = InputDesktop.Current.StartProcess(info);
        MutationGuard.RecordDispatch();
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
        TimeSpan? gracePeriod = null
    )
    {
        RefuseIfBlocked(processId, pending);
        using var lease = GuardedInput.AcquireLease();
        void Validate()
        {
            validate?.Invoke();
            GuardedInput.RequireDrivable(processId, InputDesktop.Current);
        }

        void Call() => InputDesktop.Current.CallPattern(call);
        if (pending == null)
        {
            MutationGuard.Execute(Validate, Call);
            return new PatternCallResult(PatternCallOutcome.Completed);
        }

        return ModalAwareInvoker.Execute(
            processId,
            description,
            () => MutationGuard.Execute(Validate, Call),
            pending,
            gracePeriod
        );
    }
}
