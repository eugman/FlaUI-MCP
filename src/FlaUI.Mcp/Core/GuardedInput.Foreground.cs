namespace FlaUI.Mcp.Core;

/// <summary>The foreground side of guarded input: capturing the foreground target and naming an activation failure.</summary>
public sealed partial class GuardedInput
{
    public static InputTarget ForegroundTarget(ProcessPolicy policy) =>
        ForegroundTarget(
            policy,
            () =>
            {
                var hwnd = Win32Desktop.GetForegroundWindow();
                return InputTarget.Capture(hwnd, Win32Desktop.GetProcessId(hwnd));
            }
        );

    internal static InputTarget ForegroundTarget(ProcessPolicy policy, Func<InputTarget> captureTarget)
    {
        var captured = captureTarget();
        // Authorize the captured identity, not an earlier foreground observation.
        if (!policy.IsProcessAllowed(captured.ProcessId))
        {
            var name = InputDesktop.Current.ProcessNameOf(captured.ProcessId) ?? "unknown";
            throw InputRefusedException.Record(
                new InputRefusal(
                    RefusalCodes.ProcessNotAllowed,
                    policy.DescribeDenied($"The foreground window's process '{name}'")
                        + " Focus an allowed window first, or target an element ref directly."
                )
            );
        }

        return captured;
    }

    // Windows can refuse SetForegroundWindow (foreground lock) and retrying cannot override it.
    // Only a user click or an uncontested interactive session can, so name the blocker.
    internal static InputRefusal DescribeActivationFailure(
        int targetPid,
        nint foreground,
        int foregroundPid,
        string? foregroundName
    )
    {
        var target = new ProcessRef(null, targetPid);
        if (foreground == 0)
        {
            return new InputRefusal(
                "desktop-unavailable",
                "desktop-unavailable: Windows reports no foreground window; the session may be locked, disconnected or non-interactive. "
                    + "No input sent. Observe again; unattended runs need an unlocked interactive session."
            )
            {
                Target = target,
            };
        }

        var blocker = new ProcessRef(foregroundName, foregroundPid);
        if (foregroundPid != targetPid)
        {
            return new InputRefusal(
                "activation-denied",
                $"activation-denied: Windows kept '{foregroundName ?? "unknown"}' (PID {foregroundPid}) in the foreground instead of target PID {targetPid}. "
                    + "No input sent. Attended: ask the user to click the target window's title bar, then observe before retrying. Unattended: activation cannot be forced."
            )
            {
                ForegroundProcess = blocker,
                Target = target,
            };
        }

        return new InputRefusal(
            "activation-denied",
            $"activation-denied: Windows kept another window (hwnd={foreground}) of target PID {targetPid} in the foreground. "
                + "No input sent. Focus the intended target window, then observe before retrying."
        )
        {
            ForegroundProcess = blocker,
            Target = target,
        };
    }
}
