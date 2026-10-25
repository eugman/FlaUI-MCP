using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace FlaUI.Mcp.Core;

/// <summary>
/// Manages UI Automation sessions and launched applications
/// </summary>
public class SessionManager : IDisposable
{
    private readonly UIA3Automation? _automation;
    private readonly object _gate = new();
    private bool _disposed;
    private readonly Dictionary<string, Window> _windows = new();
    private readonly Dictionary<string, nint> _windowHwnds = new();
    private readonly Dictionary<string, int> _windowPids = new();
    private readonly Dictionary<nint, string> _hwndToHandle = new();
    private readonly ProcessPolicy _processPolicy;
    private int _windowCounter = 0;
    private readonly Dictionary<string, InputTarget> _identities = new();

    public InputTarget GetInputTarget(string handle)
    {
        InputTarget target;
        lock (_gate)
        {
            target = _identities.TryGetValue(handle, out var value)
                ? value
                : throw new ArgumentException("Unknown input target.");
        }

        target.EnsureAlive();
        return target;
    }

    public ProcessIdentity? ResolveTarget(System.Text.Json.JsonElement? args, ElementRegistry refs)
    {
        // Diagnostic metadata only: no provider/process reads and no input validation.
        if (args is not { ValueKind: System.Text.Json.JsonValueKind.Object } a)
        {
            return null;
        }

        // A batch targets what its steps target: the first that names one, so a stranded batch blocks that app.
        if (a.TryGetProperty("actions", out var actions))
        {
            return actions.ValueKind == System.Text.Json.JsonValueKind.Array
                ? actions.EnumerateArray().Select(step => ResolveTarget(step, refs)).FirstOrDefault(t => t != null)
                : null;
        }

        string? handle = null;
        if (a.TryGetProperty("handle", out var h) && h.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            handle = h.GetString();
        }
        else if (
            a.TryGetProperty("ref", out var r)
            && r.ValueKind == System.Text.Json.JsonValueKind.String
            && refs.HasElement(r.GetString()!)
        )
        {
            handle = refs.WindowForRef(r.GetString()!);
        }

        lock (_gate)
        {
            return handle != null && _identities.TryGetValue(handle, out var identity)
                ? new(identity.ProcessId, identity.StartedTicks)
                : null;
        }
    }

    internal ProcessIdentity? ResolveForegroundTarget()
    {
        var (hwnd, pid) = InputTarget.ForegroundOwner();
        return ResolveNativeTarget(hwnd, pid);
    }

    /// <summary>Resolve a native window before a provider read begins.</summary>
    internal ProcessIdentity? ResolveNativeTarget(nint hwnd, int pid)
    {
        if (hwnd == 0 || pid == 0)
        {
            return null;
        }

        lock (_gate)
        {
            if (
                _hwndToHandle.TryGetValue(hwnd, out var handle)
                && _identities.TryGetValue(handle, out var identity)
                && identity.ProcessId == pid
            )
            {
                return new(pid, identity.StartedTicks);
            }
        }

        // The foreground window may not have been registered yet. Its PID still identifies
        // which provider must remain blocked if the ensuing snapshot strands.
        try
        {
            return new(pid, InputTarget.Capture(hwnd, pid).StartedTicks);
        }
        catch
        {
            return new(pid, 0);
        }
    }

    public SessionManager(ProcessPolicy? processPolicy = null)
        : this(new UIA3Automation(), processPolicy ?? ProcessPolicy.AllowAll) { }

    // A null automation supports metadata-only tests without constructing a native provider.
    internal SessionManager(UIA3Automation? automation, ProcessPolicy processPolicy)
    {
        _automation = automation;
        _processPolicy = processPolicy;
    }

    public UIA3Automation Automation =>
        _automation ?? throw new InvalidOperationException("No UI Automation provider configured.");

    public (string handle, Window window) LaunchApp(string appPath, string[]? args = null, int? waitSeconds = null)
    {
        if (!_processPolicy.IsExecutableAllowed(appPath))
        {
            throw ProcessPolicy.NotAllowed(_processPolicy.DescribeDenied($"'{appPath}'"));
        }

        var maxPolls = LaunchPollCount(waitSeconds);

        // Use Process.Start for more reliable launching
        var psi = LaunchStartInfo(appPath, args);

        using var process = WindowMutation.Start(psi);
        if (process == null)
        {
            throw ProcessPolicy.LaunchFailed($"Failed to start process: {appPath}", started: false);
        }

        // Wait for the process to be ready
        try
        {
            process.WaitForInputIdle(5000);
        }
        catch
        { /* Some processes don't support this */
        }

        Thread.Sleep(1000); // Extra wait for window to appear
        IReadOnlyList<Win32WindowInfo> Candidates() =>
            Win32Desktop.GetTopLevelWindows(process.Id).Where(w => !w.IsToolWindow && !w.IsCloaked).ToArray();
        var (window, timeouts) = WaitForLaunchWindow(process, Candidates, maxPolls);

        if (window == null)
        {
            var exited = process.HasExited;
            if (!exited)
            {
                var observed = _processPolicy.IsProcessAllowed(process.Id) ? Candidates() : [];
                throw new LaunchPendingException(appPath, process.Id, observed, timeouts > 0);
            }

            throw ProcessPolicy.LaunchFailed(
                LaunchFailure(appPath, process.Id, exited, [], timeouts > 0),
                started: true
            );
        }

        var windowHandle = RegisterWindow(window);
        return (windowHandle, window);
    }

    internal const int LaunchPolls = 40;

    internal static int LaunchPollCount(int? waitSeconds) =>
        waitSeconds is null ? LaunchPolls
        : waitSeconds is >= 1 and <= 90 ? waitSeconds.Value * 2
        : throw new ArgumentException("waitSeconds must be between 1 and 90", nameof(waitSeconds));

    internal static int LaunchSettlePolls(int maxPolls) => maxPolls < 4 ? maxPolls : 4;

    // A title or an unregistered window is not proof of launch ownership.
    // Brokered/single-instance launches must be attached explicitly by the caller.
    // Some apps show no window, or two, for several seconds while they start. A longer settle avoids returning
    // a brief splash, but cannot prove that a stable candidate is the final main window. windows_launch labels
    // its handle provisional; callers must confirm the intended window before sending input.
    private (Window? window, int timeouts) WaitForLaunchWindow(
        System.Diagnostics.Process process,
        Func<IReadOnlyList<Win32WindowInfo>> candidatesOf,
        int maxPolls
    )
    {
        Window? window = null;
        var timeouts = 0;
        nint previous = 0;
        var consecutive = 0;
        for (var attempt = 0; attempt < maxPolls && window == null && timeouts < 2; attempt++)
        {
            OperationContext.Check();
            if (process.HasExited)
            {
                break;
            }

            var candidates = candidatesOf();
            var stable = ObserveLaunchWindow(ref previous, ref consecutive, candidates, LaunchSettlePolls(maxPolls));
            if (stable is { } hwnd)
            {
                // A provider still starting up can time out; each timeout already waited, so stop after two.
                try
                {
                    window = Automation.FromHandle(hwnd)?.AsWindow();
                }
                catch (TimeoutException)
                {
                    timeouts++;
                }

                OperationContext.Check();
            }

            if (window == null)
            {
                Thread.Sleep(500);
            }
        }

        return (window, timeouts);
    }

    /// <summary>Wait for one owned window across the bounded settle polls, resetting on replacement or ambiguity.</summary>
    internal static nint? ObserveLaunchWindow(
        ref nint previous,
        ref int consecutive,
        IReadOnlyList<Win32WindowInfo> candidates,
        int requiredPolls = 4
    )
    {
        if (candidates.Count != 1)
        {
            previous = 0;
            consecutive = 0;
            return null;
        }

        var current = candidates[0].Hwnd;
        consecutive = current == previous ? consecutive + 1 : 1;
        previous = current;
        return consecutive >= requiredPolls ? current : null;
    }

    internal static string LaunchFailure(
        string appPath,
        int processId,
        bool exited,
        IReadOnlyList<string> titles,
        bool timedOut = false
    ) =>
        exited
            ? $"Launch was requested for {appPath} (PID {processId}), but that process has exited; another instance may own the window. Do not blindly relaunch. Use windows_list_windows and explicitly attach to the intended window."
            : $"Launch was requested for {appPath} (PID {processId}), but "
                + (
                    timedOut
                        ? "its window did not respond to UI Automation in time"
                        : "no unique owned window was found"
                )
                + (titles.Count > 0 ? $"; its windows are {string.Join(", ", titles.Select(t => $"\"{t}\""))}" : "")
                + ". Do not blindly relaunch. Use windows_list_windows and explicitly attach to the intended window.";

    internal static System.Diagnostics.ProcessStartInfo LaunchStartInfo(string appPath, string[]? args)
    {
        var info = new System.Diagnostics.ProcessStartInfo(appPath) { UseShellExecute = true };
        foreach (var argument in args ?? [])
        {
            info.ArgumentList.Add(argument);
        }

        return info;
    }

    public (string handle, Window window) AttachToWindow(string title)
    {
        OperationContext.Check();
        // Match top-level titles through Win32; a desktop-wide UIA search can hang on unrelated apps.
        var match = UniqueWindowByTitle(Win32Desktop.GetTopLevelWindows(), title);
        var window = match == null ? null : Automation.FromHandle(match.Hwnd)?.AsWindow();

        if (window == null)
        {
            throw ProcessPolicy.WindowNotFound($"Window not found: {title}");
        }

        var handle = RegisterWindow(window);
        return (handle, window);
    }

    internal static Win32WindowInfo? UniqueWindowByTitle(IReadOnlyList<Win32WindowInfo> windows, string title)
    {
        var matches = windows.Where(w => w.Title == title && !w.IsCloaked).Take(2).ToArray();
        if (matches.Length > 1)
        {
            throw ProcessPolicy.WindowTitleAmbiguous(
                $"More than one visible window is titled {title}. Use windows_list_windows and attach by its handle."
            );
        }

        return matches.FirstOrDefault();
    }

    public string RegisterWindow(Window window)
    {
        OperationContext.Check();
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
        catch
        { /* best effort */
        }

        if (hwnd != 0 && pid == 0)
        {
            pid = Win32Desktop.GetProcessId(hwnd);
        }

        EnsureProcessAllowed(pid);

        // Capture before publishing even when re-registering an existing HWND.
        var identity = hwnd != 0 ? InputTarget.Capture(hwnd, pid) : null;
        return PublishWindow(window, hwnd, pid, identity);
    }

    /// <summary>
    /// Register a window by its native handle only, without touching UI Automation.
    /// The UIA Window object is created lazily on first use in <see cref="GetWindow"/>.
    /// </summary>
    public string RegisterNativeWindow(nint hwnd, int processId)
    {
        OperationContext.Check();
        EnsureProcessAllowed(processId);

        var identity = InputTarget.Capture(hwnd, processId);
        return PublishWindow(null, hwnd, processId, identity);
    }

    internal string PublishWindow(Window? window, nint hwnd, int processId, InputTarget? identity)
    {
        // Native/provider reads must finish before entering the metadata gate.
        lock (_gate)
        {
            OperationContext.Check();
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (
                hwnd != 0
                && _hwndToHandle.TryGetValue(hwnd, out var existing)
                && _identities.TryGetValue(existing, out var oldIdentity)
                && oldIdentity == identity
            )
            {
                if (window != null)
                {
                    _windows[existing] = window;
                }

                if (processId != 0)
                {
                    _windowPids[existing] = processId;
                }

                return existing;
            }

            var handle = $"w{++_windowCounter}";
            if (window != null)
            {
                _windows[handle] = window;
            }

            if (identity != null)
            {
                _identities[handle] = identity;
            }

            if (hwnd != 0)
            {
                _windowHwnds[handle] = hwnd;
                _hwndToHandle[hwnd] = handle;
            }

            if (processId != 0)
            {
                _windowPids[handle] = processId;
            }

            return handle;
        }
    }

    public Window? GetWindow(string handle) =>
        GetWindow(handle, hwnd => Automation.FromHandle(hwnd)?.AsWindow(), identity => identity.EnsureAlive());

    /// <summary>Whether the handle's window is still the one registered; a stale one is forgotten, and false.</summary>
    private bool IdentityHolds(string handle, InputTarget identity, Action<InputTarget> validateIdentity)
    {
        try
        {
            validateIdentity(identity);
            return true;
        }
        catch (Exception stale)
            when (stale is ArgumentException or System.ComponentModel.Win32Exception || InputTarget.IsStale(stale))
        {
            // Handled here: the window is forgotten, so the refusal is not the call's failure.
            InputTarget.Handled(stale);
            ForgetWindow(handle);
            return false;
        }
    }

    internal Window? GetWindow(string handle, Func<nint, Window?> attach, Action<InputTarget> validateIdentity)
    {
        OperationContext.Check();
        InputTarget? identity;
        Window? window;
        nint hwnd;
        lock (_gate)
        {
            identity = _identities.GetValueOrDefault(handle);
            window = _windows.GetValueOrDefault(handle);
            hwnd = _windowHwnds.GetValueOrDefault(handle);
        }

        if (identity != null && !IdentityHolds(handle, identity, validateIdentity))
        {
            return null;
        }

        OperationContext.Check();
        if (window != null)
        {
            lock (_gate)
            {
                return ReferenceEquals(_windows.GetValueOrDefault(handle), window) ? window : null;
            }
        }

        // Lazily attach to windows registered via RegisterNativeWindow
        if (hwnd != 0)
        {
            var attached = attach(hwnd);
            lock (_gate)
            {
                OperationContext.Check();
                // Recovery may have forgotten this registration during the provider call.
                if (
                    attached == null
                    || _disposed
                    || _windowHwnds.GetValueOrDefault(handle) != hwnd
                    || _identities.GetValueOrDefault(handle) != identity
                )
                {
                    return null;
                }

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
        lock (_gate)
        {
            return _windowPids.TryGetValue(handle, out var pid) ? pid : 0;
        }
    }

    /// <summary>
    /// Get the cached native window handle for a window handle, or 0 if unknown.
    /// Never touches UI Automation.
    /// </summary>
    public nint GetWindowHwnd(string handle)
    {
        lock (_gate)
        {
            return _windowHwnds.TryGetValue(handle, out var hwnd) ? hwnd : 0;
        }
    }

    /// <summary>
    /// List top-level windows using Win32 enumeration only. This never blocks,
    /// even when an app's UI Automation provider is busy (e.g., held up by a
    /// modal dialog opened from a pending Invoke call).
    /// </summary>
    public List<(string handle, string title, string? processName)> ListWindows()
    {
        (string Handle, nint Hwnd, int Pid)[] registered;
        lock (_gate)
        {
            registered = _windowHwnds
                .Select(pair => (pair.Key, pair.Value, _windowPids.GetValueOrDefault(pair.Key)))
                .ToArray();
        }

        foreach (var (handle, hwnd, pid) in registered)
        {
            if (Win32Desktop.GetProcessId(hwnd) != pid)
            {
                ForgetWindow(handle);
            }
        }

        var result = new List<(string, string, string?)>();
        foreach (var info in Win32Desktop.GetTopLevelWindows())
        {
            if (info.Title.Length == 0 || info.IsToolWindow || info.IsCloaked)
            {
                continue;
            }

            var processName = ProcessPolicy.NameOf(info.ProcessId);

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
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Inaccessible processes are not controllable; continue listing others.
            }
            catch (ArgumentException)
            {
                // Process.GetProcessById can race process exit.
            }
            catch (Exception) when (Win32Desktop.GetProcessId(info.Hwnd) != info.ProcessId)
            {
                // Window closed or changed owner between enumeration and registration.
                // Keep other windows in the result; do not suppress unrelated failures.
            }
        }

        return result;
    }

    public void FocusWindow(string handle, PendingInvokeTracker? pending = null)
    {
        OperationContext.Check();
        // Prefer Win32 focus (never blocks); fall back to UIA for windows
        // registered before a native handle was captured.
        var hwnd = GetWindowHwnd(handle);
        if (hwnd != 0)
        {
            WindowMutation.Focus(GetInputTarget(handle), hwnd);
            return;
        }

        var window = AttachedForFallback(handle, pending);
        WindowMutation.Uia(window.Properties.ProcessId.Value, $"Focus on {handle}", window.Focus, pending);
    }

    public void CloseWindow(string handle, PendingInvokeTracker? pending = null)
    {
        OperationContext.Check();
        // Prefer a Win32 WM_CLOSE (never blocks); fall back to UIA.
        var hwnd = GetWindowHwnd(handle);
        if (hwnd == 0)
        {
            var window = AttachedForFallback(handle, pending);
            // UIA Close is also a request, not proof of disappearance.
            WindowMutation.Uia(window.Properties.ProcessId.Value, $"Close on {handle}", window.Close, pending);
            return;
        }

        // WM_CLOSE is asynchronous and may open a save prompt. Retain the
        // handle while the native window still exists so recovery can use it.
        if (!WindowMutation.Close(GetInputTarget(handle), hwnd, handle))
        {
            ForgetWindow(handle);
        }
    }

    /// <summary>
    /// The UIA window a focus or close must go through, or null when the handle has a native HWND and Win32
    /// does it. Refuses while the window's provider is blocked.
    /// </summary>
    internal Window? UiaWindowFor(string handle, PendingInvokeTracker? pending)
    {
        OperationContext.Check();
        return GetWindowHwnd(handle) != 0 ? null : AttachedForFallback(handle, pending);
    }

    /// <summary>The UIA window behind a handle with no native HWND, once its provider is known to be free.</summary>
    private Window AttachedForFallback(string handle, PendingInvokeTracker? pending)
    {
        WindowMutation.RefuseIfBlocked(GetWindowProcessId(handle), pending);
        var window = GetWindow(handle) ?? throw new ArgumentException($"Window not found: {handle}");
        EnsureProcessAllowed(window.Properties.ProcessId.Value);
        OperationContext.Check();
        return window;
    }

    internal void ForgetWindow(string handle)
    {
        lock (_gate)
        {
            _windows.Remove(handle);
            _identities.Remove(handle);
            if (_windowHwnds.TryGetValue(handle, out var removedHwnd))
            {
                if (_hwndToHandle.GetValueOrDefault(removedHwnd) == handle)
                {
                    _hwndToHandle.Remove(removedHwnd);
                }

                _windowHwnds.Remove(handle);
            }

            _windowPids.Remove(handle);
        }
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
            throw ProcessPolicy.OwnerUnknown(
                "Cannot verify this window's owning process against the app allowlist "
                    + $"({ProcessPolicy.EnvironmentVariable}), so it is not controllable."
            );
        }

        if (!_processPolicy.IsProcessAllowed(processId))
        {
            var name = ProcessPolicy.NameOf(processId) ?? $"pid {processId}";
            throw ProcessPolicy.NotAllowed(_processPolicy.DescribeDenied($"Process '{name}'"));
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _windows.Clear();
            _identities.Clear();
            _windowHwnds.Clear();
            _windowPids.Clear();
            _hwndToHandle.Clear();
        }

        _automation?.Dispose();
    }
}
