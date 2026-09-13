using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;

namespace PlaywrightWindows.Mcp.Core;

public sealed record InputTarget(nint Hwnd, int ProcessId, long StartedTicks, nint HitHwnd = 0)
{
    public void EnsureAlive()
    {
        using var process = Process.GetProcessById(ProcessId);
        if (process.StartTime.ToUniversalTime().Ticks != StartedTicks || Win32Desktop.GetProcessId(Hwnd) != ProcessId)
            throw new InvalidOperationException("Stale process/window identity.");
    }
    public static InputTarget Capture(nint hwnd, int pid)
    {
        if (hwnd == 0 || pid == 0 || Win32Desktop.GetProcessId(hwnd) != pid)
            throw new InvalidOperationException("Cannot establish window ownership.");
        using var process = Process.GetProcessById(pid);
        return new(hwnd, pid, process.StartTime.ToUniversalTime().Ticks);
    }
}

/// <summary>Physical input is global: serialize it and fail closed on target/focus drift.</summary>
public sealed class GuardedInput : IDisposable
{
    private readonly IDisposable lease;
    private readonly InputTarget target;
    public GuardedInput(InputTarget target, AutomationElement? element = null)
    {
        this.target = target;
        OperationContext.Check();
        lease = AcquireLease();
        try
        {
            CheckIdentity();
            Win32Desktop.FocusWindow(target.Hwnd);
            if (element != null) { element.Focus(); }
            Verify();
        }
        catch { lease.Dispose(); throw; }
    }

    private static readonly SemaphoreSlim InputGate = new(1, 1);
    public static IDisposable AcquireLease()
    {
        if (!InputGate.Wait(TimeSpan.FromSeconds(2), OperationContext.Current.Value?.Stop.Token ?? CancellationToken.None))
            throw new TimeoutException("Desktop input is busy; no input sent.");
        return new InputLease();
    }
    private sealed class InputLease : IDisposable
    {
        public void Dispose() => InputGate.Release();
    }
    public static InputTarget ForegroundTarget(ProcessPolicy policy)
    {
        var denied = policy.CheckForegroundWindowAllowed();
        if (denied != null) throw new InvalidOperationException(denied);
        var hwnd = Win32Desktop.GetForegroundWindow();
        return InputTarget.Capture(hwnd, Win32Desktop.GetProcessId(hwnd));
    }

    public static void Validate(InputTarget expected, nint foreground, int actualPid, long actualStart, bool enabled)
    {
        if (expected.Hwnd != foreground || expected.ProcessId != actualPid || expected.StartedTicks != actualStart || !enabled)
            throw new InvalidOperationException($"Input target/focus changed or is disabled; no further input sent. " +
                $"Expected HWND={expected.Hwnd}, PID={expected.ProcessId}, start={expected.StartedTicks}; " +
                $"foreground HWND={foreground}, target PID={actualPid}, start={actualStart}, enabled={enabled}.");
    }
    private long CheckIdentity()
    {
        using var process = Process.GetProcessById(target.ProcessId);
        var ticks = process.StartTime.ToUniversalTime().Ticks;
        if (ticks != target.StartedTicks || Win32Desktop.GetProcessId(target.Hwnd) != target.ProcessId)
            throw new InvalidOperationException("Stale process/window identity.");
        return ticks;
    }
    public void Verify()
    {
        OperationContext.Check();
        Validate(target, Win32Desktop.GetForegroundWindow(), Win32Desktop.GetProcessId(target.Hwnd), CheckIdentity(), Win32Desktop.IsWindowEnabled(target.Hwnd));
    }
    public void Send(Action input) { Verify(); input(); }
    public void Type(string text)
    {
        // Bounded chunks avoid per-character process queries while limiting drift exposure.
        var chunk = new System.Text.StringBuilder(64);
        foreach (var rune in text.EnumerateRunes())
        {
            chunk.Append(rune);
            if (chunk.Length < 64) continue;
            Send(() => Keyboard.Type(chunk.ToString()));
            chunk.Clear();
        }
        if (chunk.Length > 0) Send(() => Keyboard.Type(chunk.ToString()));
    }
    public void Click(AutomationElement element, MouseButton button = MouseButton.Left, bool doubleClick = false)
    {
        var point = element.GetClickablePoint();
        Verify();
        var clickRoot = target.HitHwnd == 0 ? target.Hwnd : target.HitHwnd;
        if (Win32Desktop.GetProcessId(clickRoot) != target.ProcessId || Win32Desktop.WindowAt(point) != clickRoot) throw new InvalidOperationException("Click point is obscured by another window.");
        Send(() => { if (doubleClick) Mouse.DoubleClick(point, button); else Mouse.Click(point, button); });
    }
    public void Dispose() => lease.Dispose();
}
