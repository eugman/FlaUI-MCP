using System.Diagnostics;
using System.Drawing;
using FlaUI.Core.AutomationElements;

namespace FlaUI.Mcp.Core;

/// <summary>A child element with the bounds and offscreen state read for it.</summary>
/// <param name="Bounds">Null when the bounds could not be read.</param>
/// <param name="IsOffscreen">Null when IsOffscreen could not be read.</param>
public sealed record ChildBox(AutomationElement Element, Rectangle? Bounds, bool? IsOffscreen);

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

    /// <summary>Moves the pointer to a screen point; one input. The fake records the point instead.</summary>
    public virtual void MovePointer(Point point) => Dispatch(() => FlaUI.Core.Input.Mouse.MoveTo(point));

    /// <summary>Where the pointer is now, in screen pixels.</summary>
    public virtual Point PointerPosition() => FlaUI.Core.Input.Mouse.Position;

    /// <summary>The bounds of every screen, in physical pixels.</summary>
    public virtual IReadOnlyList<Rectangle> Monitors() =>
        System.Windows.Forms.Screen.AllScreens.Select(s => s.Bounds).ToList();

    /// <summary>The element UI Automation reports at a screen point (FromPoint); may throw for a blocked provider.</summary>
    public virtual AutomationElement? ElementAt(AutomationElement target, Point point) =>
        target.Automation.FromPoint(point);

    public virtual AutomationElement? ParentOf(AutomationElement element) => element.Parent;

    /// <summary>The clock that pattern-call waits read their deadlines from; a fake desktop can hold it still.</summary>
    public virtual TimeProvider Time => TimeProvider.System;

    /// <summary>A process's name for a refusal, or null when it cannot be read (exited, protected, no such PID).</summary>
    public virtual string? ProcessNameOf(int processId) => ProcessPolicy.TryGetProcessName(processId);

    public virtual bool HasValuePattern(AutomationElement element) => element.Patterns.Value.IsSupported;

    /// <summary>The element UI Automation reports as having keyboard focus, or null.</summary>
    public virtual AutomationElement? FocusedElement(AutomationElement reference) =>
        reference.Automation.FocusedElement();

    /// <summary>Whether two elements are the same UI Automation element (compared by the provider).</summary>
    public virtual bool SameElement(AutomationElement a, AutomationElement b) => a.Equals(b);

    public virtual string AutomationIdOf(AutomationElement element) =>
        element.Properties.AutomationId.ValueOrDefault ?? "";

    public virtual Rectangle BoundsOf(AutomationElement element) => element.BoundingRectangle;

    public virtual FlaUI.Core.Definitions.ControlType ControlTypeOf(AutomationElement element) =>
        element.Properties.ControlType.ValueOrDefault;

    /// <summary>
    /// The children of an element with their bounds and offscreen state, read in one cached request so a list
    /// with many rows costs one round trip, not two per row.
    /// </summary>
    public virtual IReadOnlyList<ChildBox> ChildBoxes(AutomationElement parent)
    {
        var properties = parent.Automation.PropertyLibrary.Element;
        var cache = new FlaUI.Core.CacheRequest();
        cache.Add(properties.BoundingRectangle);
        cache.Add(properties.IsOffscreen);
        using (cache.Activate())
        {
            return parent
                .FindAllChildren()
                .Select(child => new ChildBox(
                    child,
                    child.Properties.BoundingRectangle.TryGetValue(out var bounds) ? bounds : null,
                    child.Properties.IsOffscreen.TryGetValue(out var offscreen) ? offscreen : null
                ))
                .ToList();
        }
    }

    /// <summary>An element as a refusal names it: "ControlType 'Name'".</summary>
    public virtual string DescribeElement(AutomationElement element) =>
        $"{element.Properties.ControlType.ValueOrDefault} '{element.Properties.Name.ValueOrDefault}'";

    /// <summary>The control with keyboard focus on the desktop, or null when Windows reports none.</summary>
    public virtual FocusedControl? FocusedControl() => Win32Desktop.GetFocusedControl();

    /// <summary>The clipboard's text, read without changing it.</summary>
    public virtual Win32Desktop.ClipboardText ClipboardText() => Win32Desktop.GetClipboardText();

    private static readonly Lazy<FlaUI.UIA3.UIA3Automation> Automation = new(() => new FlaUI.UIA3.UIA3Automation());

    /// <summary>
    /// The window's UI Automation tree as indented "ControlType 'Name' [AutomationId]" lines, at most 4 levels and
    /// 200 elements. A read; it may block on a hung provider, so callers bound it.
    /// </summary>
    public virtual string ElementTree(nint hwnd)
    {
        var lines = new List<string>();
        void Walk(AutomationElement element, int depth)
        {
            if (lines.Count >= 200)
            {
                return;
            }

            var id = element.Properties.AutomationId.ValueOrDefault;
            lines.Add(
                $"{new string(' ', depth * 2)}{element.Properties.ControlType.ValueOrDefault} '{element.Properties.Name.ValueOrDefault}'"
                    + (string.IsNullOrEmpty(id) ? "" : $" [{id}]")
            );
            if (depth < 4)
            {
                foreach (var child in element.FindAllChildren())
                {
                    Walk(child, depth + 1);
                }
            }
        }

        Walk(Automation.Value.FromHandle(hwnd), 0);
        return string.Join('\n', lines);
    }

    /// <summary>Renders a window by handle (PrintWindow); a read, so it needs no lease or grant.</summary>
    public virtual bool CaptureWindow(nint hwnd, out byte[] image, out string? reason) =>
        NativeWindowCapture.TryCaptureHwnd(hwnd, out image, out reason);

    /// <summary>The visible top-level windows of a process.</summary>
    public virtual IReadOnlyList<Win32WindowInfo> TopLevelWindows(int processId) =>
        Win32Desktop.GetTopLevelWindows(processId);

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
