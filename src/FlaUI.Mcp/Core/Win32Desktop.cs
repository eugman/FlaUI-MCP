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
    private static extern bool IsWindowEnabled(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint hWnd);

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
        if (IsIconic(hwnd))
        {
            ShowWindow(hwnd, SW_RESTORE);
        }
        SetForegroundWindow(hwnd);
    }

    /// <summary>
    /// Request a window to close by posting WM_CLOSE (non-blocking).
    /// </summary>
    public static void CloseWindow(nint hwnd)
    {
        PostMessage(hwnd, WM_CLOSE, 0, 0);
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
