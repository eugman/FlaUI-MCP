using System.Drawing;
using FlaUI.Core.AutomationElements;
using FlaUI.Mcp.Core;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// A desktop with one target window and one other window. Input is counted, never sent. Install it
/// for the duration of a test; it applies to the installing flow and the tasks it starts.
/// </summary>
internal sealed class FakeDesktop : InputDesktop
{
    public const nint TargetHwnd = 100;
    public const nint OtherHwnd = 900;
    public const int OtherPid = 4242;

    private int dispatched;

    public static int TargetPid => Environment.ProcessId;

    public static InputTarget Target => new(TargetHwnd, TargetPid, 0);

    /// <summary>The window Windows reports in the foreground before any input.</summary>
    public nint ForegroundWindow { get; set; } = TargetHwnd;

    /// <summary>After this many inputs another window takes the foreground.</summary>
    public int FocusLostAfter { get; set; } = int.MaxValue;

    /// <summary>When false, focusing the target leaves the other window in the foreground.</summary>
    public bool ActivationAllowed { get; set; } = true;

    public string State { get; set; } = "normal";

    public List<string> StateRequests { get; } = [];

    public int Dispatched => Volatile.Read(ref dispatched);

    public int FocusAttempts { get; private set; }

    public ProcessIntegrity Self { get; set; } = new(ProcessIntegrity.Medium, false);

    /// <summary>The target's token as the fake reader reports it; null when unreadable.</summary>
    public ProcessIntegrity? TargetIntegrity { get; set; } = new(ProcessIntegrity.Medium, false);

    public override ProcessIntegrity SelfIntegrity() => Self;

    public override ProcessIntegrity? Integrity(int processId) => TargetIntegrity;

    public IDisposable Install()
    {
        Override.Value = this;
        return new Uninstall();
    }

    public override void EnsureAlive(InputTarget target) { }

    public override nint Foreground() => Dispatched >= FocusLostAfter ? OtherHwnd : ForegroundWindow;

    public override void Focus(nint hwnd)
    {
        FocusAttempts++;
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

    public override nint WindowAt(Point point) => TargetHwnd;

    public override void FocusElement(AutomationElement element) { }

    public override bool HasKeyboardFocus(AutomationElement element) => true;

    public override void Dispatch(Action input) => Interlocked.Increment(ref dispatched);

    /// <summary>What PostMessage(WM_CLOSE) returns.</summary>
    public bool PostCloseResult { get; set; } = true;

    public int CloseRequests { get; private set; }

    public override bool PostClose(nint hwnd)
    {
        CloseRequests++;
        return PostCloseResult;
    }

    public override string WindowState(nint hwnd) => State;

    public override void SetWindowState(nint hwnd, string state) => StateRequests.Add(state);

    private sealed class Uninstall : IDisposable
    {
        public void Dispose() => Override.Value = null;
    }
}
