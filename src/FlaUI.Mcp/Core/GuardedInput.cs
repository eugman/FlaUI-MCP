using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;

namespace FlaUI.Mcp.Core;

public sealed record InputTarget(nint Hwnd, int ProcessId, long StartedTicks, nint HitHwnd = 0)
{
    public void EnsureAlive() => InputDesktop.Current.EnsureAlive(this);

    internal void EnsureAliveOnWindows()
    {
        using var process = Process.GetProcessById(ProcessId);
        if (process.StartTime.ToUniversalTime().Ticks != StartedTicks || Win32Desktop.GetProcessId(Hwnd) != ProcessId)
        {
            throw new InvalidOperationException("Stale process/window identity.");
        }
    }

    public static InputTarget Capture(nint hwnd, int pid)
    {
        if (hwnd == 0 || pid == 0 || InputDesktop.Current.ProcessOf(hwnd) != pid)
        {
            throw new InvalidOperationException("Cannot establish window ownership.");
        }

        using var process = Process.GetProcessById(pid);
        return new(hwnd, pid, process.StartTime.ToUniversalTime().Ticks);
    }
}

/// <summary>Physical input is global: serialize it and fail closed on target/focus drift.</summary>
public sealed class GuardedInput : IDisposable
{
    private readonly IDisposable lease;
    private readonly InputTarget target;
    private readonly InputDesktop desktop = InputDesktop.Current;

    // What this guard changed before any input, so a later refusal does not claim nothing was sent.
    private readonly string? changedFirst;

    /// <summary>What this guard changed before any input (such as "the target window was activated"), or null.</summary>
    public string? Changed => changedFirst;

    public GuardedInput(InputTarget target, AutomationElement? element = null, bool verifyFocus = false)
        : this(target, element, verifyFocus ? FocusRule.Exact : FocusRule.None) { }

    public GuardedInput(InputTarget target, AutomationElement? element, FocusRule focus)
    {
        this.target = target;
        OperationContext.Check();
        lease = AcquireLease();
        string? focusedFirst = null;
        try
        {
            target.EnsureAlive();
            MutationGuard.CheckPermission();
            RequireDrivable(target.ProcessId, desktop);
            focusedFirst = Activate(target);
            if (element != null && FocusControl(element))
            {
                focusedFirst ??= "the control was focused";
            }

            RequireActivated(target);
            Verify();
            if (focus != FocusRule.None && (element == null || !FocusRules.Holds(focus, element, desktop)))
            {
                throw new InvalidOperationException(
                    "Intended control did not receive verified keyboard focus; no input sent."
                );
            }

            changedFirst = focusedFirst;
        }
        catch (Exception error) when (focusedFirst != null && IsRefusal(error))
        {
            lease.Dispose();
            throw ChangedFirst(error, focusedFirst);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Bring the target to the foreground when another window is in front. Counted as one change when the
    /// foreground moved or the window was restored; returns a note for a later refusal, or null when nothing
    /// changed. Only the target (or its modal dialog) in front is an activation: a foreground that moved to any
    /// other window is noted as a change, and the activation check that follows refuses it.
    /// </summary>
    private string? Activate(InputTarget target)
    {
        var before = desktop.Foreground();
        if (IsTargetWindow(before, target))
        {
            return null;
        }
        // While a modal dialog disables the target, activate the dialog: a disabled window
        // cannot take input, so focusing it would only fail verification.
        var window = desktop.IsEnabled(target.Hwnd) ? target.Hwnd : desktop.LastActivePopup(target.Hwnd);
        // Focusing a minimized window restores it even when Windows then refuses to activate it.
        var stateBefore = desktop.WindowState(window);
        desktop.Focus(window);
        var after = desktop.Foreground();
        var restored = desktop.WindowState(window) != stateBefore;
        if (after == before && !restored)
        {
            return null;
        }

        MutationGuard.RecordDispatch();
        if (IsTargetWindow(after, target))
        {
            return "the target window was activated";
        }

        return restored ? "the target window was restored" : "the foreground window changed";
    }

    /// <summary>Focus the intended control. Counted as one change, and true, when it gained keyboard focus.</summary>
    private bool FocusControl(AutomationElement element)
    {
        var hadFocus = desktop.HasKeyboardFocus(element);
        desktop.FocusElement(element);
        if (hadFocus || !desktop.HasKeyboardFocus(element))
        {
            return false;
        }

        MutationGuard.RecordDispatch();
        return true;
    }

    /// <summary>A refusal or guard failure: what a change noted before it must be added to.</summary>
    internal static bool IsRefusal(Exception error) => error is InvalidOperationException or AutomationRefusal;

    /// <summary>A refusal raised after focus changed: it may not claim that nothing was sent.</summary>
    internal static Exception ChangedFirst(Exception error, string note)
    {
        var message = NoteFocusChange(error.Message, note);
        return error switch
        {
            InputRefusedException refused => InputRefusedException.Record(refused.Refusal with { Message = message }),
            AutomationRefusal other => InputRefusedException.Record(new InputRefusal(other.Code, message)),
            _ => new InvalidOperationException(message, error),
        };
    }

    /// <summary>
    /// Run a check made after this guard activated the target. Its refusal says what the guard already
    /// changed, as a refusal from the guard itself would.
    /// </summary>
    public void Check(Action check)
    {
        try
        {
            check();
        }
        catch (Exception error) when (changedFirst != null && IsRefusal(error))
        {
            throw ChangedFirst(error, changedFirst);
        }
    }

    /// <summary>Replace "no input sent" with what actually changed, or append it.</summary>
    internal static string NoteFocusChange(string message, string note)
    {
        const string NothingSent = "no input sent";
        var at = message.IndexOf(NothingSent, StringComparison.OrdinalIgnoreCase);
        if (at < 0)
        {
            return $"{message} Before this, {note}.";
        }

        var replacement = $"no keys or clicks were sent, but {note} first";
        if (char.IsUpper(message[at]))
        {
            replacement = char.ToUpperInvariant(replacement[0]) + replacement[1..];
        }

        return message[..at] + replacement + message[(at + NothingSent.Length)..];
    }

    /// <summary>The machine-wide input lease (<see cref="DesktopLease"/>): waits up to 2 s, then refuses as busy.</summary>
    public static IDisposable AcquireLease() =>
        DesktopLease.Acquire(
            TimeSpan.FromSeconds(2),
            OperationContext.Current.Value?.Stop.Token ?? CancellationToken.None
        );

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
            var name = ProcessPolicy.TryGetProcessName(captured.ProcessId) ?? "unknown";
            throw new InvalidOperationException(
                policy.DescribeDenied($"The foreground window's process '{name}'")
                    + " Focus an allowed window first, or target an element ref directly."
            );
        }

        return captured;
    }

    /// <summary>
    /// Refuse before any focus, input or window message when Windows would silently drop them: the
    /// target runs at a higher integrity level, or with UIAccess, than this server.
    /// </summary>
    internal static void RequireDrivable(int processId, InputDesktop desktop)
    {
        var self = desktop.SelfIntegrity();
        var target = desktop.Integrity(processId);
        if (!ProcessIntegrity.Blocks(self, target))
        {
            return;
        }

        var name = ProcessPolicy.TryGetProcessName(processId);
        throw InputRefusedException.Record(ProcessIntegrity.DescribeBlocked(self, target, processId, name)!);
    }

    private void RequireActivated(InputTarget target)
    {
        var foreground = desktop.Foreground();
        if (IsTargetWindow(foreground, target))
        {
            return;
        }

        var foregroundPid = foreground == 0 ? 0 : desktop.ProcessOf(foreground);
        throw InputRefusedException.Record(
            DescribeActivationFailure(
                target.ProcessId,
                foreground,
                foregroundPid,
                foregroundPid == 0 ? null : ProcessPolicy.TryGetProcessName(foregroundPid)
            )
        );
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

    public void Verify()
    {
        OperationContext.Check();
        MutationGuard.CheckPermission();
        var foreground = desktop.Foreground();
        if (
            !ForegroundAcceptable(
                target,
                foreground,
                desktop.ProcessOf,
                desktop.BelongsTo,
                desktop.IsEnabled,
                desktop.HasOwner
            )
        )
        {
            throw new InvalidOperationException("Input target/focus changed or is disabled; no further input sent.");
        }
    }

    // The target's own hosted children (e.g. a cross-process WebView2 renderer) and owned popups
    // count as the target; a separate top-level window does not, even in the same process. A modal
    // dialog counts while it disables its owner: WinForms ShowDialog creates an owned overlapped
    // window, which GA_ROOTOWNER does not follow, so the owner chain is checked for that case only.
    // A modeless owned window is still a separate window.
    internal static bool ForegroundAcceptable(
        InputTarget target,
        nint foreground,
        Func<nint, int> processOf,
        Func<nint, nint, bool> belongsTo,
        Func<nint, bool> enabled,
        Func<nint, nint, bool> ownedBy
    )
    {
        if (processOf(target.Hwnd) != target.ProcessId || foreground == 0 || !enabled(foreground))
        {
            return false;
        }

        return belongsTo(foreground, target.Hwnd) || !enabled(target.Hwnd) && ownedBy(foreground, target.Hwnd);
    }

    private bool IsTargetWindow(nint foreground, InputTarget target) =>
        desktop.BelongsTo(foreground, target.Hwnd)
        || !desktop.IsEnabled(target.Hwnd) && desktop.HasOwner(foreground, target.Hwnd);

    public void Send(Action input)
    {
        Verify();
        MutationGuard.RecordDispatch();
        desktop.Dispatch(input);
    }

    /// <summary>
    /// Undo a held button or key. It goes through the same seam and count as other input, but skips the
    /// foreground check: a release must go out even after focus moved, or the button stays held.
    /// </summary>
    public void Release(Action input)
    {
        MutationGuard.RecordDispatch();
        desktop.Dispatch(input);
    }

    /// <summary>Replace the clipboard under this guard's lease; counted when written, false when it is busy.</summary>
    public bool SetClipboardText(string text)
    {
        Verify();
        if (!desktop.SetClipboardText(text))
        {
            return false;
        }

        MutationGuard.RecordDispatch();
        return true;
    }

    /// <summary>
    /// Save the user's clipboard text and write ours under this guard's lease, counted; dispose the result to
    /// restore theirs. Null only from a fake desktop.
    /// </summary>
    public Win32Desktop.TextClipboardLease? PreserveAndSetClipboardText(string text)
    {
        Verify();
        var clipboard = desktop.PreserveAndSetClipboardText(text);
        MutationGuard.RecordDispatch();
        return clipboard;
    }

    public void Type(string text)
    {
        // Recheck foreground between chunks, without repeated process-start queries.
        var chunk = new System.Text.StringBuilder(64);
        foreach (var rune in text.EnumerateRunes())
        {
            chunk.Append(rune);
            if (chunk.Length < 64)
            {
                continue;
            }

            Send(() => Keyboard.Type(chunk.ToString()));
            chunk.Clear();
        }

        if (chunk.Length > 0)
        {
            Send(() => Keyboard.Type(chunk.ToString()));
        }
    }

    public void Click(AutomationElement element, MouseButton button = MouseButton.Left, bool doubleClick = false)
    {
        var point = default(System.Drawing.Point);
        Check(() =>
        {
            var geometry = ClickGeometry.Read(element);
            point = ClickPoint.Choose(geometry);
            // A scrollbar, header or overlay inside the target window would take the click.
            ClickPoint.Verify(point, element);
        });
        Verify();
        var clickRoot = target.HitHwnd == 0 ? target.Hwnd : target.HitHwnd;
        if (desktop.ProcessOf(clickRoot) != target.ProcessId || desktop.WindowAt(point) != clickRoot)
        {
            var covering = desktop.WindowAt(point);
            Check(() =>
                throw NotOnTarget(target, covering, "Click point is obscured by another window; no input sent.")
            );
        }

        Send(() =>
        {
            if (doubleClick)
            {
                Mouse.DoubleClick(point, button);
            }
            else
            {
                Mouse.Click(point, button);
            }
        });
    }

    /// <summary>The refusal for a pointer action whose point lies on another window, or on none.</summary>
    internal static InputRefusedException NotOnTarget(InputTarget target, nint covering, string message)
    {
        var pid = covering == 0 ? 0 : InputDesktop.Current.ProcessOf(covering);
        return InputRefusedException.Record(
            new InputRefusal("point-not-on-target", message)
            {
                ForegroundProcess = pid == 0 ? null : new ProcessRef(ProcessPolicy.TryGetProcessName(pid), pid),
                Target = new ProcessRef(null, target.ProcessId),
            }
        );
    }

    public void Dispose() => lease.Dispose();
}
