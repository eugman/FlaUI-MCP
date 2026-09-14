using System.Runtime.InteropServices;
using System.Text;

namespace PlaywrightWindows.Mcp.Core;

/// <summary>
/// Lightweight information about a top-level Win32 window, gathered without
/// any UI Automation calls.
/// </summary>
/// <param name="Hwnd">Native window handle.</param>
/// <param name="Title">Window title (may be empty).</param>
/// <param name="ProcessId">Owning process id.</param>
/// <param name="IsEnabled">Whether the window accepts input (modal dialogs disable their owner).</param>
/// <param name="IsToolWindow">Whether the window has WS_EX_TOOLWINDOW (excluded from window lists).</param>
/// <param name="IsCloaked">Whether the window is DWM-cloaked (e.g., suspended UWP apps).</param>
public sealed record Win32WindowInfo(
    nint Hwnd,
    string Title,
    int ProcessId,
    bool IsEnabled,
    bool IsToolWindow,
    bool IsCloaked);

/// <summary>
/// Win32-based desktop window enumeration and manipulation.
/// All APIs used here read cached window metadata and never block on the target
/// process's message loop, so they remain usable while a UI Automation provider
/// is blocked (e.g., by a modal dialog opened from a pending Invoke call).
/// </summary>
public static class Win32Desktop
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080;
    private const int DWMWA_CLOAKED = 14;
    private const int SW_RESTORE = 9;
    private const uint WM_CLOSE = 0x0010;

    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindowEnabled(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(nint hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int width, int height, uint flags);

    public static bool IsNormalWindow(nint hwnd) => IsWindowVisible(hwnd) && !IsIconic(hwnd) && !IsZoomed(hwnd);

    public static void PlaceWindow(nint hwnd, System.Drawing.Rectangle bounds)
    {
        // SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER. Synchronous, one attempt.
        if (!SetWindowPos(hwnd, 0, bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0004 | 0x0010 | 0x0200))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Window placement failed; mutation not replayed");
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern nint GetAncestor(nint hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(System.Drawing.Point point);

    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public int cbSize, flags;
        public nint hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
        public RECT rcCaret;
    }

    [DllImport("user32.dll")]
    private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint hWnd, StringBuilder lpClassName, int nMaxCount);

    /// <summary>
    /// Names the native control holding keyboard focus in a window's GUI thread, e.g. BUTTON "Cancel".
    /// Win32 only, so it answers while UI Automation is blocked. A control drawn inside a parent
    /// (no HWND of its own) is reported as that parent.
    /// </summary>
    public static string DescribeFocus(nint hwnd)
    {
        var thread = hwnd == 0 ? 0 : GetWindowThreadProcessId(hwnd, out _);
        var info = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
        if (thread == 0 || !GetGUIThreadInfo(thread, ref info) || info.hwndFocus == 0) return "focused element (control unknown)";
        static string Read(Func<nint, StringBuilder, int, int> read, nint target)
        {
            var text = new StringBuilder(256);
            read(target, text, text.Capacity);
            return text.Length > 60 ? text.ToString(0, 60) + "…" : text.ToString();
        }
        // WinForms classes look like WindowsForms10.BUTTON.app.0.2b89eaa_r3_ad1; the middle part is the control kind.
        var className = Read(GetClassName, info.hwndFocus).Split(".app.")[0].Replace("WindowsForms10.", "");
        var text = Read(GetWindowText, info.hwndFocus);
        return $"focused {className}{(text.Length > 0 ? $" \"{text}\"" : "")} in window \"{Read(GetWindowText, GetAncestor(info.hwndFocus, 2))}\"";
    }

    public static int GetProcessId(nint hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        return (int)pid;
    }

    public static nint WindowAt(System.Drawing.Point point) => GetAncestor(WindowFromPoint(point), 2);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint hWnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary>
    /// Enumerate visible top-level windows, optionally restricted to a single process.
    /// </summary>
    /// <param name="processId">If set, only windows owned by this process are returned.</param>
    public static IReadOnlyList<Win32WindowInfo> GetTopLevelWindows(int? processId = null)
    {
        var result = new List<Win32WindowInfo>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;

            GetWindowThreadProcessId(hwnd, out var pid);
            if (processId.HasValue && pid != (uint)processId.Value) return true;

            var titleBuilder = new StringBuilder(512);
            GetWindowText(hwnd, titleBuilder, titleBuilder.Capacity);

            var exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            var isToolWindow = (exStyle & WS_EX_TOOLWINDOW) != 0;

            var isCloaked = false;
            if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0)
            {
                isCloaked = cloaked != 0;
            }

            result.Add(new Win32WindowInfo(
                hwnd,
                titleBuilder.ToString(),
                (int)pid,
                IsWindowEnabled(hwnd),
                isToolWindow,
                isCloaked));
            return true;
        }, 0);
        return result;
    }

    /// <summary>
    /// Bring a window to the foreground, restoring it first if minimized.
    /// </summary>
    public static void FocusWindow(nint hwnd)
    {
        if (GetForegroundWindow() == hwnd) return;
        if (IsIconic(hwnd))
        {
            ShowWindow(hwnd, SW_RESTORE);
        }
        SetForegroundWindow(hwnd);
        for (var i = 0; i < 10 && GetForegroundWindow() != hwnd; i++) Thread.Sleep(25);
    }

    /// <summary>
    /// Request a window to close by posting WM_CLOSE (non-blocking).
    /// </summary>
    public static void CloseWindow(nint hwnd)
    {
        PostMessage(hwnd, WM_CLOSE, 0, 0);
    }

    /// <summary>
    /// Get the process id owning the current foreground window, or 0 if there
    /// is no foreground window.
    /// </summary>
    public static int GetForegroundWindowProcessId()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == 0)
        {
            return 0;
        }
        GetWindowThreadProcessId(hwnd, out var pid);
        return (int)pid;
    }

    /// <summary>
    /// Get a window's bounding rectangle in screen coordinates, or null on failure.
    /// </summary>
    public static System.Drawing.Rectangle? GetWindowBounds(nint hwnd)
    {
        if (!GetWindowRect(hwnd, out var rect)) return null;
        return System.Drawing.Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
    }
}
