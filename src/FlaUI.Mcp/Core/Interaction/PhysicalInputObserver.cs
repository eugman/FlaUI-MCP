using System.ComponentModel;
using System.Runtime.InteropServices;

namespace FlaUI.Mcp.Core;

/// <summary>
/// Counts non-injected input; never records keys or text, and of coordinates only where the last button went down
/// (a host can check that a click it asked the user for landed in its window). Shared by every host; it replaces
/// the copies the Power BI, Camtasia and TE3 hosts carried.
/// </summary>
/// <remarks>
/// The hooks live on a thread that runs only a message loop. A separate pulse thread posts it a beat every
/// HeartbeatMs and the loop records it, so a hook thread that stops pumping (which makes Windows drop its hooks
/// silently) shows as a missed beat, and the observer stays unavailable from then on. The pulse is a plain thread,
/// not WM_TIMER, which Windows may throttle for a background process.
/// </remarks>
public sealed class PhysicalInputObserver : IInputObserver, IInputEventSource
{
    private const int HeartbeatMs = 250;
    private const uint BeatMessage = 0x8001; // WM_APP + 1
    private const uint QuitMessage = 0x0012; // WM_QUIT

    private readonly Thread thread;
    private readonly Thread pulse;
    private readonly ManualResetEventSlim ready = new();
    private readonly ManualResetEventSlim stopping = new();
    private readonly Heartbeat heartbeat = new(Environment.TickCount64, lastInputSource: LastInputTick);
    private readonly Hook keyboard;
    private readonly Hook mouse;
    private readonly InputTally tally = new();
    private Exception? failure;
    private int disposed;
    private uint threadId;

    public PhysicalInputObserver()
    {
        keyboard = (code, message, data) => OnInput(code, message, data, true);
        mouse = (code, message, data) => OnInput(code, message, data, false);
        // Above normal, so a busy machine rarely starves the hooks or the beat past the 1 s latch.
        thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "FlaUI-MCP input observer",
            Priority = ThreadPriority.AboveNormal,
        };
        pulse = new Thread(Pulse)
        {
            IsBackground = true,
            Name = "FlaUI-MCP input observer heartbeat",
            Priority = ThreadPriority.AboveNormal,
        };
        thread.Start();
        if (StartFailure(ready, null, StartTimeout) is { } timedOut)
        {
            failure = timedOut;
        }

        if (failure != null)
        {
            Dispose();
            throw new InvalidOperationException("Cannot observe physical input; interaction remains disabled", failure);
        }

        pulse.Start();
    }

    /// <summary>How long the hook thread may take to install its hooks before the start fails.</summary>
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Why the hook thread did not start: its own failure, or a timeout when it never reported ready (a wedged
    /// thread would otherwise hold the caller forever). Null when it started.
    /// </summary>
    internal static Exception? StartFailure(ManualResetEventSlim ready, Exception? failure, TimeSpan timeout) =>
        !ready.Wait(timeout)
            ? new TimeoutException($"The input observer's hook thread did not start within {timeout.TotalSeconds:0} s.")
            : failure;

    public long Sequence
    {
        get
        {
            RequireAlive();
            return tally.Sequence;
        }
    }

    public InputEventInfo? LastEvent => tally.LastEvent;

    /// <summary>Why the observer stopped vouching for the input; null while it still does.</summary>
    public ObserverFault? Fault
    {
        get
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                return new(ObserverFault.Disposed);
            }

            return !thread.IsAlive ? new(ObserverFault.ThreadExited) : heartbeat.Fault;
        }
    }

    // The last event's kind as the hook stores it: a primitive, so the diagnostic snapshot stays lock-free.
    internal const int KeyboardKind = 1;
    private const int MouseOtherKind = 2;
    private const int MouseMoveKind = 3;
    private const int MouseButtonKind = 4;
    private const int MouseWheelKind = 5;

    internal static string? MouseKindOf(int kind) =>
        kind switch
        {
            MouseMoveKind => "move",
            MouseButtonKind => "button",
            MouseWheelKind => "wheel",
            _ => null,
        };

    // WM_MOUSEMOVE; WM_MOUSEWHEEL, WM_MOUSEHWHEEL; the button downs, ups and double-clicks in between.
    internal static int KindOf(bool keyboard, nint message) =>
        (keyboard, message) switch
        {
            (true, _) => KeyboardKind,
            (_, MouseMove) => MouseMoveKind,
            (_, 0x020A or 0x020E) => MouseWheelKind,
            (_, >= 0x0201 and <= 0x020D) => MouseButtonKind,
            _ => MouseOtherKind,
        };

    internal const int MouseMove = 0x0200;

    public InputCounts Counts
    {
        get
        {
            RequireAlive();
            return tally.Counts;
        }
    }

    private void RequireAlive()
    {
        if (Volatile.Read(ref disposed) != 0)
        {
            throw new ObserverUnavailableException(new(ObserverFault.Disposed));
        }

        if (!thread.IsAlive)
        {
            throw new ObserverUnavailableException(new(ObserverFault.ThreadExited));
        }

        if (!heartbeat.Alive(Environment.TickCount64))
        {
            throw new ObserverUnavailableException(heartbeat.Fault ?? new(ObserverFault.HeartbeatGap));
        }
    }

    // WM_LBUTTONDOWN, WM_RBUTTONDOWN, WM_MBUTTONDOWN, WM_XBUTTONDOWN.
    internal static bool IsButtonDown(nint message) => message is 0x0201 or 0x0204 or 0x0207 or 0x020B;

    // LLKHF_INJECTED / LLKHF_LOWER_IL_INJECTED and LLMHF equivalents.
    internal static bool IsPhysical(bool keyboard, uint flags) => (flags & (keyboard ? 0x12u : 0x03u)) == 0;

    private nint OnInput(int code, nint message, nint data, bool isKeyboard)
    {
        if (code >= 0)
        {
            tally.Observe(isKeyboard, message, data, Environment.TickCount64);
        }

        return CallNextHookEx(0, code, message, data);
    }

    /// <summary>Physical mouse moves the drift filter tolerated (not counted) so far.</summary>
    public long ToleratedMoves => tally.ToleratedMoves;

    private void Run()
    {
        nint keyHook = 0,
            mouseHook = 0;
        try
        {
            // Creates this thread's message queue, then publishes the id: a WM_QUIT posted to the id always lands.
            PeekMessage(out _, 0, 0, 0, 0);
            Volatile.Write(ref threadId, GetCurrentThreadId());
            // A Dispose that came first (a start that timed out) posted nowhere; it set stopping, so stop here,
            // before any hook is installed.
            if (stopping.IsSet)
            {
                return;
            }

            // The clock started at construction, but no hook exists until the next line, so nothing Windows could
            // drop has been missed yet: a slow thread start must not poison the observer at birth. Installing the
            // hooks is still measured (the beat before ready), since a hook is live while the second one installs.
            heartbeat.Restart(Environment.TickCount64);
            keyHook = SetWindowsHookEx(13, keyboard, GetModuleHandle(null), 0);
            if (keyHook == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            mouseHook = SetWindowsHookEx(14, mouse, GetModuleHandle(null), 0);
            if (mouseHook == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            if (stopping.IsSet)
            {
                return;
            }

            heartbeat.Beat(Environment.TickCount64);
            ready.Set();
            Pump();
        }
        catch (Exception error)
        {
            failure = error;
        }
        finally
        {
            if (keyHook != 0)
            {
                UnhookWindowsHookEx(keyHook);
            }

            if (mouseHook != 0)
            {
                UnhookWindowsHookEx(mouseHook);
            }

            ready.Set();
        }
    }

    // The hooks are called from inside GetMessage; a beat is recorded only when the loop gets to it.
    private void Pump()
    {
        while (GetMessage(out var message, 0, 0, 0) > 0)
        {
            if (message.Message == BeatMessage)
            {
                heartbeat.Beat(Environment.TickCount64);
                continue;
            }

            TranslateMessage(in message);
            DispatchMessage(in message);
        }
    }

    private void Pulse()
    {
        while (!stopping.Wait(HeartbeatMs))
        {
            PostThreadMessage(Volatile.Read(ref threadId), BeatMessage, 0, 0);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        // Stopping is set before the id is read: a thread that has not published its id yet sees it and exits.
        stopping.Set();
        var observer = Volatile.Read(ref threadId);
        if (thread.IsAlive && observer != 0)
        {
            PostThreadMessage(observer, QuitMessage, 0, 0); // on the observer thread only
        }

        if (Thread.CurrentThread != thread)
        {
            thread.Join(TimeSpan.FromSeconds(2));
        }
    }

    private delegate nint Hook(int code, nint message, nint data);

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public nint Hwnd;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    // Diagnostics only (the heartbeat's latch): whether Windows saw any input, injected or not, during a gap.
    private static uint? LastInputTick()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        return GetLastInputInfo(ref info) ? info.Time : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int id, Hook hook, nint module, uint threadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? name);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint threadId, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Msg message, nint hwnd, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out Msg message, nint hwnd, uint filterMin, uint filterMax, uint remove);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(in Msg message);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessage(in Msg message);
}
