using System.Drawing;
using FlaUI.Core.AutomationElements;
using FlaUI.Mcp.Core;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>
/// The fake's answers to Core's element, pointer, screen, clipboard and capture reads: inert and deterministic, so a
/// Core call made from a TE3 test never falls through to the real desktop.
/// </summary>
internal sealed partial class FakeDesktop
{
    /// <summary>
    /// The desktop's clock, Core's manual one: it moves only when a Core wait pauses (or a test advances it), so a
    /// still clock can never hold a poll, and nothing sleeps for real.
    /// </summary>
    public FlaUI.Mcp.Tests.ManualTimeProvider Clock { get; } = new();

    public override TimeProvider Time => Clock;

    public override void Pause(TimeSpan delay) => Clock.Advance(delay);

    /// <summary>Where the pointer is; moves are counted as input.</summary>
    public Point Pointer { get; private set; }

    public override void MovePointer(Point point)
    {
        Interlocked.Increment(ref dispatched);
        Pointer = point;
    }

    public override Point PointerPosition() => Pointer;

    public override IReadOnlyList<Rectangle> Monitors() => [new Rectangle(0, 0, 1920, 1080)];

    public override IReadOnlyList<Win32WindowInfo> TopLevelWindows(int processId) => [];

    // Every point is on the element asked about, and each element stands alone.
    public override AutomationElement? ElementAt(AutomationElement target, Point point) => target;

    public override AutomationElement? ParentOf(AutomationElement element) => null;

    public override bool SameElement(AutomationElement a, AutomationElement b) => ReferenceEquals(a, b);

    public override string? ProcessNameOf(int processId) =>
        processId == TargetPid ? System.Diagnostics.Process.GetCurrentProcess().ProcessName : null;

    public override bool HasValuePattern(AutomationElement element) => false;

    // HasKeyboardFocus is always true here, so the focused element is the one asked about.
    public override AutomationElement? FocusedElement(AutomationElement reference) => reference;

    public override string AutomationIdOf(AutomationElement element) => "";

    public override string ClassNameOf(AutomationElement element) => "";

    public override ClickGeometry Geometry(AutomationElement element) => new(false, null, BoundsOf(element), [], []);

    public override string? ScrollIntoView(
        AutomationElement element,
        int processId,
        PendingInvokeTracker pending,
        Action validate,
        InputTarget? target = null
    ) => null;

    public override Rectangle BoundsOf(AutomationElement element) => new(100, 100, 800, 600);

    public override FlaUI.Core.Definitions.ControlType ControlTypeOf(AutomationElement element) =>
        FlaUI.Core.Definitions.ControlType.Custom;

    public override IReadOnlyList<ChildBox> ChildBoxes(AutomationElement parent) => [];

    public override string DescribeElement(AutomationElement element) => "fake element";

    public override FocusedControl? FocusedControl() => null;

    public override Win32Desktop.ClipboardText ClipboardText() => new(null, false, "offline fake desktop");

    public override string ElementTree(nint hwnd) => "";

    public override bool CaptureWindow(nint hwnd, out byte[] image, out string? reason)
    {
        image = [];
        reason = "offline fake desktop";
        return false;
    }
}
