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
                InputDesktop.Current.Focus(hwnd);
                return true;
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

    /// <summary>Starting a process changes the desktop like input does: same lease and permission check.</summary>
    public static Process? Start(ProcessStartInfo info)
    {
        Process? started = null;
        using var lease = GuardedInput.AcquireLease();
        MutationGuard.Execute(OperationContext.Check, () => started = Process.Start(info));
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
    /// provider that blocks returns as pending instead of hanging the tool.
    /// </summary>
    public static void Uia(int processId, string description, Action call, PendingInvokeTracker? pending)
    {
        RefuseIfBlocked(processId, pending);
        using var lease = GuardedInput.AcquireLease();
        void Validate() => GuardedInput.RequireDrivable(processId, InputDesktop.Current);
        if (pending == null)
        {
            MutationGuard.Execute(Validate, call);
            return;
        }

        ModalAwareInvoker.Execute(processId, description, () => MutationGuard.Execute(Validate, call), pending);
    }
}
