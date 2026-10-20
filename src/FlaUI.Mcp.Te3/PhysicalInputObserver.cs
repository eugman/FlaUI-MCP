using System.ComponentModel;
using System.Runtime.InteropServices;

/// <summary>
/// Counts non-injected input; never records keys, coordinates, or text. The same observer as the Camtasia and
/// Power BI hosts; Core does not expose one. TODO(core): share it from Core.
/// </summary>
internal sealed class PhysicalInputObserver : IDisposable
{
    private readonly Thread thread;
    private readonly ManualResetEventSlim ready = new();
    private readonly Hook keyboard;
    private readonly Hook mouse;
    private System.Windows.Forms.ApplicationContext? context;
    private Exception? failure;
    private long sequence;
    private int disposed;
    private uint threadId;

    public PhysicalInputObserver()
    {
        keyboard = (code, message, data) => OnInput(code, message, data, true);
        mouse = (code, message, data) => OnInput(code, message, data, false);
        thread = new Thread(Run) { IsBackground = true, Name = "TE3 input observer" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        if (failure != null)
        {
            Dispose();
            throw new InvalidOperationException("Cannot observe physical input; interaction remains disabled", failure);
        }
    }

    public long Sequence
    {
        get
        {
            if (!thread.IsAlive || Volatile.Read(ref disposed) != 0)
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
            context = new System.Windows.Forms.ApplicationContext();
            threadId = GetCurrentThreadId();
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

            ready.Set();
            System.Windows.Forms.Application.Run(context);
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

            context?.Dispose();
            ready.Set();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        if (thread.IsAlive)
        {
            PostThreadMessage(threadId, 0x0012, 0, 0); // WM_QUIT on observer thread only
        }

        if (Thread.CurrentThread != thread)
        {
            thread.Join(TimeSpan.FromSeconds(2));
        }
    }

    private delegate nint Hook(int code, nint message, nint data);

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
}
