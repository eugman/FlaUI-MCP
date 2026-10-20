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

    public virtual void EnsureAlive(InputTarget target) => target.EnsureAliveOnWindows();

    public virtual nint Foreground() => Win32Desktop.GetForegroundWindow();

    public virtual void Focus(nint hwnd) => Win32Desktop.FocusWindow(hwnd);

    public virtual bool IsEnabled(nint hwnd) => Win32Desktop.IsWindowEnabled(hwnd);

    public virtual nint LastActivePopup(nint owner) => Win32Desktop.LastActivePopup(owner);

    public virtual int ProcessOf(nint hwnd) => Win32Desktop.GetProcessId(hwnd);

    public virtual bool BelongsTo(nint hwnd, nint target) => Win32Desktop.BelongsTo(hwnd, target);

    public virtual bool HasOwner(nint hwnd, nint owner) => Win32Desktop.HasOwner(hwnd, owner);

    public virtual nint WindowAt(Point point) => Win32Desktop.WindowAt(point);

    public virtual void FocusElement(AutomationElement element) => element.Focus();

    public virtual bool HasKeyboardFocus(AutomationElement element) =>
        element.Properties.HasKeyboardFocus.TryGetValue(out var focused) && focused;

    /// <summary>Sends one input. The fake records it instead.</summary>
    public virtual void Dispatch(Action input) => input();

    public virtual string WindowState(nint hwnd) => Win32Desktop.GetWindowState(hwnd);

    public virtual void SetWindowState(nint hwnd, string state) => Win32Desktop.SetWindowState(hwnd, state);
}
