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

    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;
    private const nint HWND_MESSAGE = -3;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(nint hWndNewOwner);

    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetClipboardData(uint format, nint handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterClipboardFormat(string name);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(int exStyle, string className, string? windowName, int style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint hWnd);

    [DllImport("kernel32.dll")]
    private static extern nint GlobalAlloc(uint flags, nuint bytes);

    [DllImport("kernel32.dll")]
    private static extern nint GlobalLock(nint memory);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(nint memory);

    [DllImport("kernel32.dll")]
    private static extern nint GlobalFree(nint memory);

    /// <summary>
    /// Replace the clipboard with Unicode text, excluded from clipboard history.
    /// A message-only window owns it, since an ownerless EmptyClipboard makes SetClipboardData fail.
    /// Returns false when another process keeps the clipboard open.
    /// </summary>
    public static bool SetClipboardText(string text)
    {
        var owner = CreateWindowEx(0, "STATIC", null, 0, 0, 0, 0, 0, HWND_MESSAGE, 0, 0, 0);
        try
        {
            var opened = false;
            for (var attempt = 0; attempt < 10 && !(opened = OpenClipboard(owner)); attempt++) Thread.Sleep(50);
            if (!opened) return false;
            try
            {
                EmptyClipboard();
                SetClipboardBytes(CF_UNICODETEXT, Encoding.Unicode.GetBytes(text + "\0"));
                SetClipboardBytes(RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing"), new byte[4]);
                return true;
            }
            finally { CloseClipboard(); }
        }
        finally { if (owner != 0) DestroyWindow(owner); }
    }

    private static void SetClipboardBytes(uint format, byte[] bytes)
    {
        var memory = GlobalAlloc(GMEM_MOVEABLE, (nuint)bytes.Length);
        if (memory == 0) throw new InvalidOperationException("Clipboard memory allocation failed.");
        Marshal.Copy(bytes, 0, GlobalLock(memory), bytes.Length);
        GlobalUnlock(memory);
        // On success the clipboard owns the memory.
        if (SetClipboardData(format, memory) == 0)
        {
            GlobalFree(memory);
            throw new InvalidOperationException("Writing to the clipboard failed.");
        }
    }

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
