using FlaUI.Core.AutomationElements;

namespace PlaywrightWindows.Mcp.Core;

/// <summary>
/// Maps element refs (like "w1e5") to AutomationElements
/// Refs are scoped to windows and regenerated on each snapshot
/// </summary>
public class ElementRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, long> _generations = new();
    private readonly Dictionary<string, AutomationElement> _elements = new();
    private readonly Dictionary<string, int> _windowCounters = new();
    private readonly Dictionary<string, int> _windowProcessIds = new();
    private readonly Dictionary<string, InputTarget> _identities = new();

    public void SetWindowIdentity(string handle, int pid, nint hwnd, long? generation = null)
    {
        // UIA content and its hosting HWND can belong to different processes.
        // Keep the UIA PID for pending-provider guards; input owns the native host.
        var identity = InputTarget.Capture(hwnd, Win32Desktop.GetProcessId(hwnd));
        lock (_gate)
        {
            CheckGeneration(handle, generation);
            _windowProcessIds[handle] = pid;
            _identities[handle] = identity;
        }
    }
    public string WindowForRef(string reference)
    {
        var separator = reference.LastIndexOf('e');
        if (separator <= 0 || !HasElement(reference)) throw new ArgumentException("Unknown element ref.");
        return reference[..separator];
    }
    public void ValidateReference(string reference, string? handle = null)
    {
        if (handle != null && WindowForRef(reference) != handle) throw new ArgumentException("Handle/ref ownership mismatch.");
        var owner = WindowForRef(reference);
        InputTarget? identity;
        lock (_gate) identity = _identities.GetValueOrDefault(owner);
        identity?.EnsureAlive();
    }
    public InputTarget InputForRef(string reference)
    {
        InputTarget owner;
        lock (_gate) owner = _identities.TryGetValue(WindowForRef(reference), out var identity) ? identity : throw new InvalidOperationException("Refresh snapshot/find to establish input ownership.");
        owner.EnsureAlive();
        // Owned popups can be registered under a main-window ref namespace. Resolve the actual native root.
        var element = GetElement(reference) ?? throw new InvalidOperationException("Stale element reference.");
        for (var current = element; current != null; current = current.Parent)
        {
            if (current.Properties.ControlType.ValueOrDefault != FlaUI.Core.Definitions.ControlType.Window) continue;
            var native = current.Properties.NativeWindowHandle.ValueOrDefault;
            if (native == 0) continue;
            var root = Win32Desktop.GetAncestor(native, 2);
            if (Win32Desktop.GetProcessId(root) == owner.ProcessId)
                return owner with { Hwnd = root };
            // A cross-process frame is not a popup owned by this input target.
            break;
        }
        return owner;
    }

    /// <summary>
    /// Clear all elements for a window (called before new snapshot)
    /// </summary>
    public void ClearWindow(string windowHandle)
        => BeginSnapshot(windowHandle);

    public long BeginSnapshot(string windowHandle)
    {
        lock (_gate)
        {
            OperationContext.Check();
            var generation = _generations.GetValueOrDefault(windowHandle) + 1;
            _generations[windowHandle] = generation;
            var prefix = windowHandle + "e";
            var keysToRemove = _elements.Keys.Where(k => k.StartsWith(prefix)).ToList();
            foreach (var key in keysToRemove)
            {
                _elements.Remove(key);
            }
            // Never recycle refs: an old snapshot must not silently target a new control.
            return generation;
        }
    }

    public void CheckGeneration(string windowHandle, long? generation)
    {
        lock (_gate)
        {
            OperationContext.Check();
            if (generation != null && _generations.GetValueOrDefault(windowHandle) != generation)
                throw new OperationCanceledException("Snapshot superseded by a newer observation; refresh refs.");
        }
    }

    /// <summary>
    /// Register an element and return its ref
    /// </summary>
    public string Register(string windowHandle, AutomationElement element, long? generation = null)
    {
        lock (_gate)
        {
            CheckGeneration(windowHandle, generation);
            if (!_windowCounters.ContainsKey(windowHandle))
            {
                _windowCounters[windowHandle] = 0;
            }

            var refId = $"{windowHandle}e{++_windowCounters[windowHandle]}";
            _elements[refId] = element;
            return refId;
        }
    }

    /// <summary>
    /// Get an element by its ref
    /// </summary>
    public AutomationElement? GetElement(string refId)
    {
        lock (_gate) return _elements.TryGetValue(refId, out var element) ? element : null;
    }

    /// <summary>
    /// Check if a ref exists
    /// </summary>
    public bool HasElement(string refId)
    {
        lock (_gate) return _elements.ContainsKey(refId);
    }

    /// <summary>
    /// Record the process id owning a window's elements. Called during snapshot
    /// building (when the provider is known to be responsive) so tools can later
    /// check for a blocked provider without touching UI Automation.
    /// </summary>
    public void SetWindowProcessId(string windowHandle, int processId)
    {
        lock (_gate)
        {
            OperationContext.Check();
            _windowProcessIds[windowHandle] = processId;
        }
    }

    /// <summary>
    /// Get the process id for an element ref (e.g. "w1e5" -> pid of window "w1").
    /// Returns 0 if unknown.
    /// </summary>
    public int GetProcessIdForRef(string refId)
    {
        var separator = refId.LastIndexOf('e');
        if (separator <= 0) return 0;
        var windowHandle = refId[..separator];
        lock (_gate) return _windowProcessIds.TryGetValue(windowHandle, out var pid) ? pid : 0;
    }
}
