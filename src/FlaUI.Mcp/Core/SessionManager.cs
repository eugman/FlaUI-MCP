using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using FlaUIApplication = FlaUI.Core.Application;

namespace PlaywrightWindows.Mcp.Core;

/// <summary>
/// Manages UI Automation sessions and launched applications
/// </summary>
public class SessionManager : IDisposable
{
    private readonly UIA3Automation _automation;
    private readonly Dictionary<string, FlaUIApplication> _applications = new();
    private readonly Dictionary<string, Window> _windows = new();
    private readonly Dictionary<string, nint> _windowHwnds = new();
    private readonly Dictionary<string, int> _windowPids = new();
    private readonly Dictionary<nint, string> _hwndToHandle = new();
    private int _windowCounter = 0;

    public SessionManager()
    {
        _automation = new UIA3Automation();
    }

    public UIA3Automation Automation => _automation;

    public (string handle, Window window) LaunchApp(string appPath, string[]? args = null)
    {
        // Use Process.Start for more reliable launching
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = appPath,
            Arguments = args != null ? string.Join(" ", args) : "",
            UseShellExecute = true
        };

        var process = System.Diagnostics.Process.Start(psi);
        if (process == null)
        {
            throw new Exception($"Failed to start process: {appPath}");
        }

        // Wait for the process to be ready
        try
        {
            process.WaitForInputIdle(5000);
        }
        catch { /* Some processes don't support this */ }

        Thread.Sleep(1000); // Extra wait for window to appear

        // Find window by process ID from desktop
        var desktop = _automation.GetDesktop();
        Window? window = null;

        // Try to find by process ID first
        var element = desktop.FindFirstDescendant(cf => cf.ByProcessId(process.Id));
        if (element != null)
        {
            window = element.AsWindow();
        }

        // If not found, the app might have spawned a different process (common for UWP)
        // Search by waiting for a new window
        if (window == null)
        {
            // Get window count before
            var existingTitles = new HashSet<string>(
                _windows.Values.Select(w => w.Title).Where(t => !string.IsNullOrEmpty(t))
            );

            // Wait and look for new windows
            for (int i = 0; i < 10 && window == null; i++)
            {
                Thread.Sleep(500);
                var windows = desktop.FindAllChildren(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Window));
                foreach (var w in windows)
                {
                    var win = w.AsWindow();
                    if (win != null && !string.IsNullOrEmpty(win.Title))
                    {
                        // Check if this looks like our app
                        var title = win.Title.ToLowerInvariant();
                        var appName = Path.GetFileNameWithoutExtension(appPath).ToLowerInvariant();
                        if (title.Contains(appName) || !existingTitles.Contains(win.Title))
                        {
                            window = win;
                            break;
                        }
                    }
                }
            }
        }

        if (window == null)
        {
            throw new Exception($"Could not find window for {appPath}. Try using windows_list_windows and windows_focus instead.");
        }

        var windowHandle = RegisterWindow(window);
        return (windowHandle, window);
    }

    public (string handle, Window window) AttachToWindow(string title)
    {
        var desktop = _automation.GetDesktop();
        var window = desktop.FindFirstDescendant(cf => cf.ByName(title))?.AsWindow();

        if (window == null)
        {
            throw new Exception($"Window not found: {title}");
        }

        var handle = RegisterWindow(window);
        return (handle, window);
    }

    public string RegisterWindow(Window window)
    {
        // Capture the native handle and process id while the provider is
        // responsive, so later operations (focus, close, blocked-provider
        // checks) can work without any UI Automation round-trips.
        nint hwnd = 0;
        var pid = 0;
        try
        {
            hwnd = window.Properties.NativeWindowHandle.ValueOrDefault;
            pid = window.Properties.ProcessId.ValueOrDefault;
        }
        catch { /* best effort */ }

        if (hwnd != 0 && _hwndToHandle.TryGetValue(hwnd, out var existing))
        {
            _windows[existing] = window;
            return existing;
        }

        var handle = $"w{++_windowCounter}";
        _windows[handle] = window;
        if (hwnd != 0)
        {
            _windowHwnds[handle] = hwnd;
            _hwndToHandle[hwnd] = handle;
        }
        if (pid != 0)
        {
            _windowPids[handle] = pid;
        }
        return handle;
    }

    /// <summary>
    /// Register a window by its native handle only, without touching UI Automation.
    /// The UIA Window object is created lazily on first use in <see cref="GetWindow"/>.
    /// </summary>
    public string RegisterNativeWindow(nint hwnd, int processId)
    {
        if (_hwndToHandle.TryGetValue(hwnd, out var existing))
        {
            _windowPids[existing] = processId;
            return existing;
        }

        var handle = $"w{++_windowCounter}";
        _windowHwnds[handle] = hwnd;
        _hwndToHandle[hwnd] = handle;
        _windowPids[handle] = processId;
        return handle;
    }

    public Window? GetWindow(string handle)
    {
        if (_windows.TryGetValue(handle, out var window))
        {
            return window;
        }

        // Lazily attach to windows registered via RegisterNativeWindow
        if (_windowHwnds.TryGetValue(handle, out var hwnd))
        {
            var attached = _automation.FromHandle(hwnd)?.AsWindow();
            if (attached != null)
            {
                _windows[handle] = attached;
                return attached;
            }
        }

        return null;
    }

    /// <summary>
    /// Get the cached process id for a window handle, or 0 if unknown.
    /// Never touches UI Automation.
    /// </summary>
    public int GetWindowProcessId(string handle)
    {
        return _windowPids.TryGetValue(handle, out var pid) ? pid : 0;
    }

    /// <summary>
    /// Get the cached native window handle for a window handle, or 0 if unknown.
    /// Never touches UI Automation.
    /// </summary>
    public nint GetWindowHwnd(string handle)
    {
        return _windowHwnds.TryGetValue(handle, out var hwnd) ? hwnd : 0;
    }

    /// <summary>
    /// List top-level windows using Win32 enumeration only. This never blocks,
    /// even when an app's UI Automation provider is busy (e.g., held up by a
    /// modal dialog opened from a pending Invoke call).
    /// </summary>
    public List<(string handle, string title, string? processName)> ListWindows()
    {
        var result = new List<(string, string, string?)>();
        foreach (var info in Win32Desktop.GetTopLevelWindows())
        {
            if (info.Title.Length == 0 || info.IsToolWindow || info.IsCloaked)
            {
                continue;
            }

            var handle = RegisterNativeWindow(info.Hwnd, info.ProcessId);

            string? processName = null;
            try
            {
                processName = System.Diagnostics.Process.GetProcessById(info.ProcessId).ProcessName;
            }
            catch { }

            result.Add((handle, info.Title, processName));
        }
        return result;
    }

    public void FocusWindow(string handle)
    {
        // Prefer Win32 focus (never blocks); fall back to UIA for windows
        // registered before a native handle was captured.
        if (_windowHwnds.TryGetValue(handle, out var hwnd))
        {
            Win32Desktop.FocusWindow(hwnd);
            return;
        }

        var window = GetWindow(handle);
        if (window == null)
        {
            throw new Exception($"Window not found: {handle}");
        }
        window.Focus();
    }

    public void CloseWindow(string handle)
    {
        // Prefer a Win32 WM_CLOSE (never blocks); fall back to UIA.
        if (_windowHwnds.TryGetValue(handle, out var hwnd))
        {
            Win32Desktop.CloseWindow(hwnd);
        }
        else
        {
            var window = GetWindow(handle);
            if (window == null)
            {
                throw new Exception($"Window not found: {handle}");
            }
            window.Close();
        }

        _windows.Remove(handle);
        if (_windowHwnds.TryGetValue(handle, out var removedHwnd))
        {
            _hwndToHandle.Remove(removedHwnd);
            _windowHwnds.Remove(handle);
        }
        _windowPids.Remove(handle);
    }

    public void Dispose()
    {
        foreach (var app in _applications.Values)
        {
            try { app.Close(); } catch { }
        }
        _applications.Clear();
        _windows.Clear();
        _windowHwnds.Clear();
        _windowPids.Clear();
        _hwndToHandle.Clear();
        _automation.Dispose();
    }
}
