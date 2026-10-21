using System.Drawing;
using FlaUI.Core.AutomationElements;
using FlaUI.Mcp.Core;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// Tests that take the process-wide input lease, some on purpose for seconds. They run alone so they
/// never make another test's input wait for the lease.
/// </summary>
[Xunit.CollectionDefinition(Name, DisableParallelization = true)]
public sealed class InputLeaseCollection
{
    public const string Name = "Input lease";
}

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

    /// <summary>Runs when guarded code revalidates the target's identity; throw from it for a stale target.</summary>
    public Action? OnEnsureAlive { get; set; }

    public override void EnsureAlive(InputTarget target) => OnEnsureAlive?.Invoke();

    public override nint Foreground() => Dispatched >= FocusLostAfter ? OtherHwnd : ForegroundWindow;

    public override void Focus(nint hwnd)
    {
        FocusAttempts++;
        // Like Windows: focusing a minimized window restores it, even when activation is then refused.
        if (ForegroundWindow != hwnd && State == WindowStates.Minimized)
        {
            State = WindowStates.Normal;
        }

        if (ActivationAllowed)
        {
            ForegroundWindow = hwnd;
        }
    }

    /// <summary>When false, the target is disabled, as while it shows a modal dialog.</summary>
    public bool TargetEnabled { get; set; } = true;

    /// <summary>A modal dialog owned by the target, or 0 for none.</summary>
    public nint ModalHwnd { get; set; }

    /// <summary>Whether the intended control reports keyboard focus after it is focused.</summary>
    public bool KeyboardFocus { get; set; } = true;

    public int ElementFocusRequests { get; private set; }

    public override bool IsEnabled(nint hwnd) => hwnd != TargetHwnd || TargetEnabled;

    public override nint LastActivePopup(nint owner) => ModalHwnd == 0 ? owner : ModalHwnd;

    /// <summary>The process that owns the other window.</summary>
    public int OtherProcessId { get; set; } = OtherPid;

    public override int ProcessOf(nint hwnd) => hwnd == OtherHwnd ? OtherProcessId : TargetPid;

    public override bool BelongsTo(nint hwnd, nint target) => hwnd != 0 && hwnd == target;

    public override bool HasOwner(nint hwnd, nint owner) => hwnd != 0 && hwnd == ModalHwnd && owner == TargetHwnd;

    /// <summary>Which window is under a point; the target everywhere unless a test says otherwise.</summary>
    public Func<Point, nint> WindowUnder { get; set; } = _ => TargetHwnd;

    public override nint WindowAt(Point point) => WindowUnder(point);

    /// <summary>Whether focusing the control gives it keyboard focus.</summary>
    public bool ElementTakesFocus { get; set; } = true;

    private readonly HashSet<AutomationElement> ignoresFocus = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Make one control ignore focus requests, as a canvas or tree row that never takes keyboard focus does:
    /// it never reports focus, while the rest of the desktop keeps whatever focus it had.
    /// </summary>
    public void IgnoreFocus(AutomationElement element) => ignoresFocus.Add(element);

    public override void FocusElement(AutomationElement element)
    {
        ElementFocusRequests++;
        if (!ignoresFocus.Contains(element))
        {
            KeyboardFocus |= ElementTakesFocus;
        }
    }

    public override bool HasKeyboardFocus(AutomationElement element) =>
        KeyboardFocus && !ignoresFocus.Contains(element);

    public override void Dispatch(Action input) => Interlocked.Increment(ref dispatched);

    /// <summary>What PostMessage(WM_CLOSE) returns.</summary>
    public bool PostCloseResult { get; set; } = true;

    /// <summary>The text placed on the clipboard, or null when nothing was.</summary>
    public string? ClipboardText { get; private set; }

    /// <summary>When true, another window takes the foreground as soon as the clipboard is written.</summary>
    public bool FocusLostOnClipboardWrite { get; set; }

    /// <summary>When true, another process holds the clipboard open and writes fail.</summary>
    public bool ClipboardBusy { get; set; }

    public override Win32Desktop.TextClipboardLease? PreserveAndSetClipboardText(string text)
    {
        ClipboardText = text;
        return null;
    }

    public override bool SetClipboardText(string text)
    {
        if (ClipboardBusy)
        {
            return false;
        }

        ClipboardText = text;
        if (FocusLostOnClipboardWrite)
        {
            ForegroundWindow = OtherHwnd;
        }

        return true;
    }

    public int CloseRequests { get; private set; }

    public override bool PostClose(nint hwnd)
    {
        CloseRequests++;
        return PostCloseResult;
    }

    public override string WindowState(nint hwnd) => State;

    public override void SetWindowState(nint hwnd, string state) => StateRequests.Add(state);

    /// <summary>When true, starting a process fails as for a missing executable; nothing starts either way.</summary>
    public bool StartFails { get; set; }

    public int StartRequests { get; private set; }

    /// <summary>When true, the start returns null, as Process.Start does when no new process was started.</summary>
    public bool StartsNothing { get; set; }

    public int PatternCalls { get; private set; }

    public override System.Diagnostics.Process? StartProcess(System.Diagnostics.ProcessStartInfo info)
    {
        StartRequests++;
        if (StartFails)
        {
            throw new System.ComponentModel.Win32Exception(2);
        }

        // An unstarted Process object stands in for a started one; null is Windows starting nothing.
        return StartsNothing ? null : new System.Diagnostics.Process();
    }

    public override void CallPattern(Action call)
    {
        PatternCalls++;
        call();
    }

    public bool NormalWindow { get; set; } = true;

    public Rectangle Bounds { get; set; } = new(100, 100, 800, 600);

    public List<Rectangle> Placements { get; } = [];

    public override bool IsNormalWindow(nint hwnd) => NormalWindow;

    public override Rectangle? WindowBounds(nint hwnd) => Bounds;

    public override void PlaceWindow(nint hwnd, Rectangle bounds)
    {
        Placements.Add(bounds);
        Bounds = bounds;
    }

    /// <summary>The screens the desktop reports, in physical pixels.</summary>
    public List<Rectangle> MonitorBounds { get; set; } = [new(0, 0, 1920, 1080)];

    public override IReadOnlyList<Rectangle> Monitors() => MonitorBounds;

    /// <summary>Where the pointer is; a counted pointer move puts it on its point.</summary>
    public Point Pointer { get; set; }

    public override Point PointerPosition() => Pointer;

    public override void MovePointer(Point point)
    {
        Interlocked.Increment(ref dispatched);
        Pointer = point;
    }

    private sealed class Uninstall : IDisposable
    {
        public void Dispose() => Override.Value = null;
    }
}
