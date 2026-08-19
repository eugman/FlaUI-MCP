using FlaUI.Core.AutomationElements;

namespace PlaywrightWindows.Mcp.Core;

/// <summary>
/// Maps element refs (like "w1e5") to AutomationElements
/// Refs are scoped to windows and regenerated on each snapshot
/// </summary>
public class ElementRegistry
{
    private readonly Dictionary<string, AutomationElement> _elements = new();
    private readonly Dictionary<string, int> _windowCounters = new();
    private readonly Dictionary<string, int> _windowProcessIds = new();

    /// <summary>
    /// Clear all elements for a window (called before new snapshot)
    /// </summary>
    public void ClearWindow(string windowHandle)
    {
        var prefix = windowHandle + "e";
        var keysToRemove = _elements.Keys.Where(k => k.StartsWith(prefix)).ToList();
        foreach (var key in keysToRemove)
        {
            _elements.Remove(key);
        }
        _windowCounters[windowHandle] = 0;
    }

    /// <summary>
    /// Register an element and return its ref
    /// </summary>
    public string Register(string windowHandle, AutomationElement element)
    {
        if (!_windowCounters.ContainsKey(windowHandle))
        {
            _windowCounters[windowHandle] = 0;
        }

        var refId = $"{windowHandle}e{++_windowCounters[windowHandle]}";
        _elements[refId] = element;
        return refId;
    }

    /// <summary>
    /// Get an element by its ref
    /// </summary>
    public AutomationElement? GetElement(string refId)
    {
        return _elements.TryGetValue(refId, out var element) ? element : null;
    }

    /// <summary>
    /// Check if a ref exists
    /// </summary>
    public bool HasElement(string refId)
    {
        return _elements.ContainsKey(refId);
    }

    /// <summary>
    /// Record the process id owning a window's elements. Called during snapshot
    /// building (when the provider is known to be responsive) so tools can later
    /// check for a blocked provider without touching UI Automation.
    /// </summary>
    public void SetWindowProcessId(string windowHandle, int processId)
    {
        _windowProcessIds[windowHandle] = processId;
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
        return _windowProcessIds.TryGetValue(windowHandle, out var pid) ? pid : 0;
    }
}
