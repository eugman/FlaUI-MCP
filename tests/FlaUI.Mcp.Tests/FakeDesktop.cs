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

        if (FocusLandsOn != 0)
        {
            ForegroundWindow = FocusLandsOn;
        }
        else if (ActivationAllowed)
        {
            ForegroundWindow = hwnd;
        }
    }

    /// <summary>When set, focusing any window brings this one to the foreground instead, as a popup can.</summary>
    public nint FocusLandsOn { get; set; }

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

    // Like Windows: no window, no process.
    public override int ProcessOf(nint hwnd) =>
        hwnd switch
        {
            0 => 0,
            OtherHwnd => OtherProcessId,
            _ => TargetPid,
        };

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

    /// <summary>After this many inputs every control loses keyboard focus, as when a popup takes it.</summary>
    public int ElementFocusLostAfter { get; set; } = int.MaxValue;

    public override bool HasKeyboardFocus(AutomationElement element) =>
        KeyboardFocus && !ignoresFocus.Contains(element) && Dispatched < ElementFocusLostAfter;

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

    /// <summary>Where a requested placement lands; exact unless a test clamps it, as DPI or a minimum size can.</summary>
    public Func<Rectangle, Rectangle> PlaceAs { get; set; } = bounds => bounds;

    public override void PlaceWindow(nint hwnd, Rectangle bounds)
    {
        Placements.Add(bounds);
        Bounds = PlaceAs(bounds);
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

    /// <summary>The visible top-level windows of a process; none unless a test says otherwise.</summary>
    public Func<int, IReadOnlyList<Win32WindowInfo>> WindowsOf { get; set; } = _ => [];

    public override IReadOnlyList<Win32WindowInfo> TopLevelWindows(int processId) => WindowsOf(processId);

    /// <summary>The element UI Automation reports under a point; null for the target itself.</summary>
    public Func<Point, AutomationElement?>? HitTest { get; set; }

    /// <summary>Each element's parent, for the hit test's ancestor walk.</summary>
    public Dictionary<AutomationElement, AutomationElement> Parents { get; } = new(ReferenceEqualityComparer.Instance);

    /// <summary>How a refusal names an element, as "ControlType 'Name'".</summary>
    public Dictionary<AutomationElement, string> Descriptions { get; } = new(ReferenceEqualityComparer.Instance);

    public override AutomationElement? ElementAt(AutomationElement target, Point point) =>
        HitTest == null ? target : HitTest(point);

    public override AutomationElement? ParentOf(AutomationElement element) =>
        Parents.TryGetValue(element, out var parent) ? parent : null;

    public override bool SameElement(AutomationElement a, AutomationElement b) => ReferenceEquals(a, b);

    /// <summary>The element UI Automation reports as focused; null for none.</summary>
    public AutomationElement? FocusedOn { get; set; }

    public override AutomationElement? FocusedElement(AutomationElement reference) => FocusedOn;

    /// <summary>Each element's control type; Custom unless a test says otherwise.</summary>
    public Dictionary<AutomationElement, FlaUI.Core.Definitions.ControlType> ControlTypes { get; } =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>Each element's bounds; empty unless a test says otherwise.</summary>
    public Dictionary<AutomationElement, Rectangle> ElementBounds { get; } = new(ReferenceEqualityComparer.Instance);

    public override FlaUI.Core.Definitions.ControlType ControlTypeOf(AutomationElement element) =>
        ControlTypes.TryGetValue(element, out var type) ? type : FlaUI.Core.Definitions.ControlType.Custom;

    /// <summary>Each element's AutomationId; empty unless a test says otherwise.</summary>
    public Dictionary<AutomationElement, string> AutomationIds { get; } = new(ReferenceEqualityComparer.Instance);

    public override string AutomationIdOf(AutomationElement element) =>
        AutomationIds.TryGetValue(element, out var id) ? id : "";

    public override Rectangle BoundsOf(AutomationElement element) =>
        ElementBounds.TryGetValue(element, out var bounds) ? bounds : Rectangle.Empty;

    /// <summary>Elements that support the Value pattern.</summary>
    public HashSet<AutomationElement> ValueBearing { get; } = new(ReferenceEqualityComparer.Instance);

    public override bool HasValuePattern(AutomationElement element) => ValueBearing.Contains(element);

    /// <summary>Elements UI Automation reports as offscreen.</summary>
    public HashSet<AutomationElement> Offscreen { get; } = new(ReferenceEqualityComparer.Instance);

    public override IReadOnlyList<ChildBox> ChildBoxes(AutomationElement parent) =>
        Parents
            .Where(pair => ReferenceEquals(pair.Value, parent))
            .Select(pair => new ChildBox(
                pair.Key,
                ElementBounds.TryGetValue(pair.Key, out var bounds) ? bounds : Rectangle.Empty,
                Offscreen.Contains(pair.Key)
            ))
            .ToList();

    public override string DescribeElement(AutomationElement element) =>
        Descriptions.TryGetValue(element, out var description) ? description : "Custom ''";

    /// <summary>What rendering a window by handle returns; null for a render that fails.</summary>
    public byte[]? WindowImage { get; set; } = [];

    /// <summary>When set, a render waits for it, as PrintWindow does on a hung window.</summary>
    public ManualResetEventSlim? RenderBlock { get; set; }

    /// <summary>The UI Automation tree the fake reports for a window.</summary>
    public string Tree { get; set; } = "";

    public override string ElementTree(nint hwnd) => Tree;

    public override bool CaptureWindow(nint hwnd, out byte[] image, out string? reason)
    {
        RenderBlock?.Wait();
        image = WindowImage ?? [];
        reason = WindowImage == null ? "no image (fake)" : null;
        return WindowImage != null;
    }

    private sealed class Uninstall : IDisposable
    {
        public void Dispose() => Override.Value = null;
    }
}
