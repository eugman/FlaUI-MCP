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
public sealed class PhysicalInputObserver : IInputObserver
{
    private const int HeartbeatMs = 250;
    private const uint BeatMessage = 0x8001; // WM_APP + 1
    private const uint QuitMessage = 0x0012; // WM_QUIT

    private readonly Thread thread;
    private readonly Thread pulse;
    private readonly ManualResetEventSlim ready = new();
    private readonly ManualResetEventSlim stopping = new();
    private readonly Heartbeat heartbeat = new(Environment.TickCount64);
    private readonly Hook keyboard;
    private readonly Hook mouse;
    private Exception? failure;
    private long sequence;
    private long keys;
    private long buttonDowns;
    private long lastButtonDown = long.MinValue;
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
            return Interlocked.Read(ref sequence);
        }
    }

    public InputCounts Counts
    {
        get
        {
            RequireAlive();
            var point = Interlocked.Read(ref lastButtonDown);
            return new(
                Interlocked.Read(ref keys),
                Interlocked.Read(ref buttonDowns),
                point == long.MinValue ? null : new System.Drawing.Point((int)(point >> 32), (int)point)
            );
        }
    }

    private void RequireAlive()
    {
        if (!thread.IsAlive || Volatile.Read(ref disposed) != 0 || !heartbeat.Alive(Environment.TickCount64))
        {
            throw new InvalidOperationException("Physical input observer is unavailable");
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
            // KBDLLHOOKSTRUCT: vkCode, scanCode, flags. MSLLHOOKSTRUCT: POINT, mouseData, flags.
            var flags = unchecked((uint)Marshal.ReadInt32(data, isKeyboard ? 8 : 12));
            if (IsPhysical(isKeyboard, flags))
            {
                Interlocked.Increment(ref sequence);
                Count(isKeyboard, message, data);
            }
        }

        return CallNextHookEx(0, code, message, data);
    }

    private void Count(bool isKeyboard, nint message, nint data)
    {
        if (isKeyboard)
        {
            Interlocked.Increment(ref keys);
        }
        else if (IsButtonDown(message))
        {
            // MSLLHOOKSTRUCT starts with the POINT (x, y) in screen coordinates.
            var x = Marshal.ReadInt32(data, 0);
            var y = Marshal.ReadInt32(data, 4);
            Interlocked.Exchange(ref lastButtonDown, ((long)x << 32) | (uint)y);
            Interlocked.Increment(ref buttonDowns);
        }
    }

    private void Run()
    {
        nint keyHook = 0,
            mouseHook = 0;
        try
        {
            threadId = GetCurrentThreadId();
            // Creates this thread's message queue before anyone posts to it.
            PeekMessage(out _, 0, 0, 0, 0);
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
            PostThreadMessage(threadId, BeatMessage, 0, 0);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        stopping.Set();
        if (thread.IsAlive)
        {
            PostThreadMessage(threadId, QuitMessage, 0, 0); // on the observer thread only
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
