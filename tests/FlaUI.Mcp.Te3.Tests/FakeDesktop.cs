using System.Drawing;
using FlaUI.Core.AutomationElements;
using FlaUI.Mcp.Core;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>
/// A desktop with one target window (this process) and one other window. Input is counted, never sent. Install it
/// for the duration of a test; it applies to the installing flow and the tasks it starts.
/// </summary>
internal sealed partial class FakeDesktop : InputDesktop
{
    public const nint TargetHwnd = 100;
    public const nint OtherHwnd = 900;
    public const int OtherPid = 4242;

    private int dispatched;

    public static int TargetPid => Environment.ProcessId;

    public static InputTarget Target => new(TargetHwnd, TargetPid, 0);

    /// <summary>The window Windows reports in the foreground.</summary>
    public nint ForegroundWindow { get; set; } = TargetHwnd;

    /// <summary>When false, focusing the target leaves the other window in the foreground.</summary>
    public bool ActivationAllowed { get; set; } = true;

    /// <summary>Which window is under a point; the target everywhere unless a test says otherwise.</summary>
    public Func<Point, nint> WindowUnder { get; set; } = _ => TargetHwnd;

    public int Dispatched => Volatile.Read(ref dispatched);

    public int FocusRequests { get; private set; }

    public int CloseRequests { get; private set; }

    public int StartRequests { get; private set; }

    public IDisposable Install()
    {
        Override.Value = this;
        return new Uninstall();
    }

    public override void EnsureAlive(InputTarget target) { }

    public override nint Foreground() => ForegroundWindow;

    public override void Focus(nint hwnd)
    {
        FocusRequests++;
        if (ActivationAllowed)
        {
            ForegroundWindow = hwnd;
        }
    }

    public override bool IsEnabled(nint hwnd) => true;

    public override nint LastActivePopup(nint owner) => owner;

    public override int ProcessOf(nint hwnd) => hwnd == OtherHwnd ? OtherPid : TargetPid;

    public override bool BelongsTo(nint hwnd, nint target) => hwnd != 0 && hwnd == target;

    public override bool HasOwner(nint hwnd, nint owner) => false;

    public override nint WindowAt(Point point) => WindowUnder(point);

    public override void FocusElement(AutomationElement element) { }

    public override bool HasKeyboardFocus(AutomationElement element) => true;

    public override void Dispatch(Action input) => Interlocked.Increment(ref dispatched);

    public override bool PostClose(nint hwnd)
    {
        CloseRequests++;
        return true;
    }

    public override bool SetClipboardText(string text) => true;

    // Nothing is written or preserved: the fake has no clipboard, and Core reads null as "could not set it".
    public override Win32Desktop.TextClipboardLease? PreserveAndSetClipboardText(string text) => null;

    public override System.Diagnostics.Process? StartProcess(System.Diagnostics.ProcessStartInfo info)
    {
        StartRequests++;
        return null;
    }

    public override void CallPattern(Action call) => Interlocked.Increment(ref dispatched);

    public override string WindowState(nint hwnd) => WindowStates.Normal;

    public override bool IsNormalWindow(nint hwnd) => true;

    public override Rectangle? WindowBounds(nint hwnd) => new Rectangle(100, 100, 800, 600);

    public override void PlaceWindow(nint hwnd, Rectangle bounds) { }

    public override void SetWindowState(nint hwnd, string state) { }

    public override ProcessIntegrity SelfIntegrity() => new(ProcessIntegrity.Medium, false);

    public override ProcessIntegrity? Integrity(int processId) => new(ProcessIntegrity.Medium, false);

    private sealed class Uninstall : IDisposable
    {
        public void Dispose() => Override.Value = null;
    }
}
