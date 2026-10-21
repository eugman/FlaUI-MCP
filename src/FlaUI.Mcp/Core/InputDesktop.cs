using System.Diagnostics;
using System.Drawing;
using FlaUI.Core.AutomationElements;

namespace FlaUI.Mcp.Core;

/// <summary>
/// The desktop that guarded input reads and drives. Production uses Windows. Offline tests install a
/// fake through <see cref="Override"/>, so an input path runs end to end without sending anything.
/// </summary>
internal class InputDesktop
{
    internal static readonly AsyncLocal<InputDesktop?> Override = new();
    private static readonly InputDesktop Windows = new();

    public static InputDesktop Current => Override.Value ?? Windows;

    /// <summary>
    /// Set to 1 by the offline test assembly. The real desktop then refuses every change, so a test that forgot
    /// to install a fake, or a mutant that skips a guard, fails instead of typing into the window in front.
    /// </summary>
    internal const string OfflineVariable = "FLAUI_OFFLINE_TESTS";

    private static void RefuseOffline()
    {
        if (Environment.GetEnvironmentVariable(OfflineVariable) == "1")
        {
            throw new InvalidOperationException(
                $"The real desktop takes no input while {OfflineVariable}=1; install a fake desktop. No input sent."
            );
        }
    }

    public virtual void EnsureAlive(InputTarget target) => target.EnsureAliveOnWindows();

    public virtual nint Foreground() => Win32Desktop.GetForegroundWindow();

    public virtual void Focus(nint hwnd)
    {
        RefuseOffline();
        Win32Desktop.FocusWindow(hwnd);
    }

    public virtual bool IsEnabled(nint hwnd) => Win32Desktop.IsWindowEnabled(hwnd);

    public virtual nint LastActivePopup(nint owner) => Win32Desktop.LastActivePopup(owner);

    public virtual int ProcessOf(nint hwnd) => Win32Desktop.GetProcessId(hwnd);

    public virtual bool BelongsTo(nint hwnd, nint target) => Win32Desktop.BelongsTo(hwnd, target);

    public virtual bool HasOwner(nint hwnd, nint owner) => Win32Desktop.HasOwner(hwnd, owner);

    public virtual nint WindowAt(Point point) => Win32Desktop.WindowAt(point);

    public virtual void FocusElement(AutomationElement element)
    {
        RefuseOffline();
        element.Focus();
    }

    public virtual bool HasKeyboardFocus(AutomationElement element) =>
        element.Properties.HasKeyboardFocus.TryGetValue(out var focused) && focused;

    /// <summary>Posts WM_CLOSE; false when Windows did not accept the message.</summary>
    public virtual bool PostClose(nint hwnd)
    {
        RefuseOffline();
        return Win32Desktop.CloseWindow(hwnd);
    }

    /// <summary>Replaces the clipboard with text; false when another process holds it open.</summary>
    public virtual bool SetClipboardText(string text)
    {
        RefuseOffline();
        return Win32Desktop.SetClipboardText(text);
    }

    /// <summary>Saves the user's clipboard text, writes ours, and restores theirs when the lease is disposed.</summary>
    public virtual Win32Desktop.TextClipboardLease? PreserveAndSetClipboardText(string text)
    {
        RefuseOffline();
        return Win32Desktop.PreserveAndSetClipboardText(text);
    }

    /// <summary>Starts a process. Offline, nothing starts.</summary>
    public virtual Process? StartProcess(ProcessStartInfo info)
    {
        RefuseOffline();
        return Process.Start(info);
    }

    /// <summary>
    /// Makes one UI Automation call that changes the app: Invoke, Toggle, Select, Expand, Collapse, SetValue,
    /// ScrollIntoView, or a UI Automation focus or close.
    /// </summary>
    public virtual void CallPattern(Action call)
    {
        RefuseOffline();
        call();
    }

    /// <summary>Sends one input. The fake records it instead.</summary>
    public virtual void Dispatch(Action input)
    {
        RefuseOffline();
        input();
    }

    public virtual string WindowState(nint hwnd) => Win32Desktop.GetWindowState(hwnd);

    /// <summary>Visible and neither minimized nor maximized.</summary>
    public virtual bool IsNormalWindow(nint hwnd) => Win32Desktop.IsNormalWindow(hwnd);

    public virtual Rectangle? WindowBounds(nint hwnd) => Win32Desktop.GetWindowBounds(hwnd);

    public virtual void PlaceWindow(nint hwnd, Rectangle bounds)
    {
        RefuseOffline();
        Win32Desktop.PlaceWindow(hwnd, bounds);
    }

    public virtual void SetWindowState(nint hwnd, string state)
    {
        RefuseOffline();
        Win32Desktop.SetWindowState(hwnd, state);
    }

    private static readonly Lazy<ProcessIntegrity> Self = new(() =>
        ProcessIntegrity.Read(Environment.ProcessId) ?? new(ProcessIntegrity.Medium, false)
    );

    /// <summary>This server's own integrity level and UIAccess flag.</summary>
    public virtual ProcessIntegrity SelfIntegrity() => Self.Value;

    /// <summary>A target process's integrity, or null when its token cannot be read.</summary>
    public virtual ProcessIntegrity? Integrity(int processId) => ProcessIntegrity.Read(processId);
}
