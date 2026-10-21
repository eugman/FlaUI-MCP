using System.ComponentModel;
using System.Runtime.InteropServices;

/// <summary>
/// Counts non-injected input; never records keys, coordinates, or text. The same observer as the Camtasia and
/// Power BI hosts; Core does not expose one. TODO(core): share it from Core.
/// </summary>
/// <remarks>
/// The hooks live on a thread that runs only a message loop. A separate pulse thread posts it a beat every
/// HeartbeatMs and the loop records it, so a hook thread that stops pumping (which makes Windows drop its hooks
/// silently) shows as a missed beat, and the observer stays unavailable from then on. The pulse is a plain thread,
/// not WM_TIMER, which Windows may throttle for a background process.
/// </remarks>
internal sealed class PhysicalInputObserver : IInputObserver
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
    private int disposed;
    private uint threadId;

    public PhysicalInputObserver()
    {
        keyboard = (code, message, data) => OnInput(code, message, data, true);
        mouse = (code, message, data) => OnInput(code, message, data, false);
        thread = new Thread(Run) { IsBackground = true, Name = "TE3 input observer" };
        pulse = new Thread(Pulse) { IsBackground = true, Name = "TE3 input observer heartbeat" };
        thread.Start();
        ready.Wait();
        if (failure != null)
        {
            Dispose();
            throw new InvalidOperationException("Cannot observe physical input; interaction remains disabled", failure);
        }

        pulse.Start();
    }

    public long Sequence
    {
        get
        {
            if (!thread.IsAlive || Volatile.Read(ref disposed) != 0 || !heartbeat.Alive(Environment.TickCount64))
            {
                throw new InvalidOperationException("Physical input observer is unavailable");
            }

            return Interlocked.Read(ref sequence);
        }
    }

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
            }
        }

        return CallNextHookEx(0, code, message, data);
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
