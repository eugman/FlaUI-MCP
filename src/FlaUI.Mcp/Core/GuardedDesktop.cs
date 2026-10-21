using System.Diagnostics;

namespace FlaUI.Mcp.Core;

/// <summary>
/// The guarded way for product code to make changes it would otherwise make raw. Each call takes the input
/// lease and the mutation-permission check, counts against the running operation, and goes through
/// <see cref="InputDesktop"/>, so FLAUI_OFFLINE_TESTS refuses it. Do not call these while holding a
/// <see cref="GuardedInput"/>: they take the lease themselves. Under a guard, use its clipboard methods.
/// </summary>
public static class GuardedDesktop
{
    /// <summary>Start a process; counted only when a process is returned. A start that throws or returns null counts nothing.</summary>
    public static Process? StartProcess(ProcessStartInfo info) => WindowMutation.Start(info);

    /// <summary>
    /// Make a UI Automation call that changes the app (Invoke, Toggle, Select, Expand, Collapse, SetValue, ...).
    /// Refused while <paramref name="processId"/>'s provider is still held by an earlier call. Under the lease it
    /// runs <paramref name="validate"/>, then the integrity check, counts, and makes the call modal-aware, so a
    /// handler that opens a dialog returns as pending instead of hanging.
    /// </summary>
    public static PatternCallResult CallPattern(
        int processId,
        string description,
        Action call,
        PendingInvokeTracker pending,
        Action? validate = null,
        TimeSpan? gracePeriod = null
    ) => WindowMutation.Uia(processId, description, call, pending, validate, gracePeriod);

    /// <summary>Replace the clipboard with text; counted when written, false when another process holds it.</summary>
    public static bool SetClipboardText(string text)
    {
        OperationContext.Check();
        using var lease = GuardedInput.AcquireLease();
        MutationGuard.CheckPermission();
        if (!InputDesktop.Current.SetClipboardText(text))
        {
            return false;
        }

        MutationGuard.RecordDispatch();
        return true;
    }
}
