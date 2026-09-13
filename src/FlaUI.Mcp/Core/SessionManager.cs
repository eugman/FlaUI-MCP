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
    private readonly ProcessPolicy _processPolicy;
    private int _windowCounter = 0;
    private readonly Dictionary<string, InputTarget> _identities = new();
    public InputTarget GetInputTarget(string handle)
    {
        var target = _identities.TryGetValue(handle, out var value) ? value : throw new ArgumentException("Unknown input target.");
        target.EnsureAlive(); return target;
    }
    public int ResolveProcess(System.Text.Json.JsonElement? args, ElementRegistry refs)
        => ResolveTarget(args, refs)?.ProcessId ?? 0;

    public ProcessIdentity? ResolveTarget(System.Text.Json.JsonElement? args, ElementRegistry refs)
        => new TargetValidator(h => { if (_identities.TryGetValue(h, out var t)) { t.EnsureAlive(); return new(t.ProcessId, t.StartedTicks); } return new(GetWindowProcessId(h), 0); },
            r => { if (!refs.HasElement(r)) throw new ArgumentException("Unknown element ref."); return refs.WindowForRef(r); }).Resolve(args);

    public SessionManager(ProcessPolicy? processPolicy = null)
    {
        _automation = new UIA3Automation();
        _processPolicy = processPolicy ?? ProcessPolicy.AllowAll;
    }

    public UIA3Automation Automation => _automation;

    public (string handle, Window window) LaunchApp(string appPath, string[]? args = null)
    {
        if (!_processPolicy.IsExecutableAllowed(appPath))
        {
            throw new Exception(_processPolicy.DescribeDenied($"'{appPath}'"));
        }

        // Use Process.Start for more reliable launching
        var psi = LaunchStartInfo(appPath, args);

        OperationContext.Check();
        using var process = System.Diagnostics.Process.Start(psi);
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
        Window? window = null;
        // A title or an unregistered window is not proof of launch ownership.
        // Brokered/single-instance launches must be attached explicitly by the caller.
        for (var attempt = 0; attempt < 10 && window == null; attempt++)
        {
            OperationContext.Check();
            if (process.HasExited) break;
            var candidates = Win32Desktop.GetTopLevelWindows(process.Id)
                .Where(w => !w.IsToolWindow && !w.IsCloaked).ToArray();
            if (candidates.Length == 1)
            {
                window = _automation.FromHandle(candidates[0].Hwnd)?.AsWindow();
                OperationContext.Check();
            }
            if (window == null) Thread.Sleep(500);
        }

        if (window == null)
        {
            throw new Exception($"Launch was requested for {appPath} (PID {process.Id}), but no unique owned window was found. Do not blindly relaunch. Use windows_list_windows and explicitly attach to the intended window.");
        }

        var windowHandle = RegisterWindow(window);
        return (windowHandle, window);
    }

    internal static System.Diagnostics.ProcessStartInfo LaunchStartInfo(string appPath, string[]? args)
    {
        var info = new System.Diagnostics.ProcessStartInfo(appPath) { UseShellExecute = true };
        foreach (var argument in args ?? []) info.ArgumentList.Add(argument);
        return info;
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

        if (hwnd != 0 && pid == 0) pid = Win32Desktop.GetProcessId(hwnd);
        EnsureProcessAllowed(pid);

        // Capture before publishing even when re-registering an existing HWND.
        var identity = hwnd != 0 ? InputTarget.Capture(hwnd, pid) : null;
        if (hwnd != 0 && _hwndToHandle.TryGetValue(hwnd, out var existing) &&
            _identities.TryGetValue(existing, out var oldIdentity) && oldIdentity == identity)
        {
            _windows[existing] = window;
            return existing;
        }

        // Capture can fail if the window closes; publish no partial registration.
        var handle = $"w{++_windowCounter}";
        _windows[handle] = window;
        if (identity != null) _identities[handle] = identity;
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
        EnsureProcessAllowed(processId);

        var identity = InputTarget.Capture(hwnd, processId);
        if (_hwndToHandle.TryGetValue(hwnd, out var existing) &&
            _identities.TryGetValue(existing, out var oldIdentity) && oldIdentity == identity)
        {
            _windowPids[existing] = processId;
            return existing;
        }

        var handle = $"w{++_windowCounter}";
        _windowHwnds[handle] = hwnd;
        _identities[handle] = identity;
        _hwndToHandle[hwnd] = handle;
        _windowPids[handle] = processId;
        return handle;
    }

    public Window? GetWindow(string handle)
    {
        if (_identities.ContainsKey(handle)) GetInputTarget(handle);
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

            var processName = ProcessPolicy.TryGetProcessName(info.ProcessId);

            // When an allowlist is active, windows of other apps are not listed
            // at all - no handle is registered, so they stay unreachable.
            if (!_processPolicy.IsNameAllowed(processName))
            {
                continue;
            }

            try
            {
                var handle = RegisterNativeWindow(info.Hwnd, info.ProcessId);
                result.Add((handle, info.Title, processName));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) when (Win32Desktop.GetProcessId(info.Hwnd) != info.ProcessId)
            {
                // Window closed or changed owner between enumeration and registration.
                // Keep other windows in the result; do not suppress unrelated failures.
            }
        }
        return result;
    }

    public void FocusWindow(string handle)
    {
        OperationContext.Check();
        // Prefer Win32 focus (never blocks); fall back to UIA for windows
        // registered before a native handle was captured.
        if (_windowHwnds.TryGetValue(handle, out var hwnd))
        {
            GetInputTarget(handle);
            Win32Desktop.FocusWindow(hwnd);
            return;
        }

        var window = GetWindow(handle);
        if (window == null)
        {
            throw new Exception($"Window not found: {handle}");
        }
        EnsureProcessAllowed(window.Properties.ProcessId.Value);
        OperationContext.Check();
        window.Focus();
    }

    public void CloseWindow(string handle)
    {
        OperationContext.Check();
        // Prefer a Win32 WM_CLOSE (never blocks); fall back to UIA.
        if (_windowHwnds.TryGetValue(handle, out var hwnd))
        {
            GetInputTarget(handle);
            Win32Desktop.CloseWindow(hwnd);
            // WM_CLOSE is asynchronous and may open a save prompt. Retain the
            // handle while the native window still exists so recovery can use it.
            if (Win32Desktop.GetProcessId(hwnd) != 0) return;
        }
        else
        {
            var window = GetWindow(handle);
            if (window == null)
            {
                throw new Exception($"Window not found: {handle}");
            }
            EnsureProcessAllowed(window.Properties.ProcessId.Value);
            OperationContext.Check();
            window.Close();
            // UIA Close is also a request, not proof of disappearance.
            return;
        }

        _windows.Remove(handle);
        _identities.Remove(handle);
        if (_windowHwnds.TryGetValue(handle, out var removedHwnd))
        {
            _hwndToHandle.Remove(removedHwnd);
            _windowHwnds.Remove(handle);
        }
        _windowPids.Remove(handle);
    }

    /// <summary>
    /// Throw when an app allowlist is active and the process is not on it (or
    /// cannot be identified). Every window-handle registration funnels through
    /// this, so refs and handles can only ever point at allowed apps.
    /// </summary>
    private void EnsureProcessAllowed(int processId)
    {
        if (!_processPolicy.IsRestricted)
        {
            return;
        }

        if (processId == 0)
        {
            throw new Exception(
                "Cannot verify this window's owning process against the app allowlist " +
                $"({ProcessPolicy.EnvironmentVariable}), so it is not controllable.");
        }

        if (!_processPolicy.IsProcessAllowed(processId))
        {
            var name = ProcessPolicy.TryGetProcessName(processId) ?? $"pid {processId}";
            throw new Exception(_processPolicy.DescribeDenied($"Process '{name}'"));
        }
    }

    public void Dispose()
    {
        foreach (var app in _applications.Values)
        {
            try { app.Close(); } catch { }
        }
        _applications.Clear();
        _windows.Clear();
        _identities.Clear();
        _windowHwnds.Clear();
        _windowPids.Clear();
        _hwndToHandle.Clear();
        _automation.Dispose();
    }
}
