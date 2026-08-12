using System.Text.Json;
using FlaUI.Core.AutomationElements;
using PlaywrightWindows.Mcp.Core;

namespace PlaywrightWindows.Mcp.Tools;

/// <summary>
/// Take accessibility snapshot of a window - THE KEY TOOL FOR AGENTS
/// </summary>
public class SnapshotTool : ToolBase
{
    private readonly SessionManager _sessionManager;
    private readonly ElementRegistry _elementRegistry;
    private readonly SnapshotBuilder _snapshotBuilder;
    private readonly PendingInvokeTracker _invokeTracker;

    public SnapshotTool(SessionManager sessionManager, ElementRegistry elementRegistry, PendingInvokeTracker? invokeTracker = null)
    {
        _sessionManager = sessionManager;
        _elementRegistry = elementRegistry;
        _snapshotBuilder = new SnapshotBuilder(elementRegistry);
        _invokeTracker = invokeTracker ?? new PendingInvokeTracker();
    }

    public override string Name => "windows_snapshot";

    public override string Description => 
        "Capture accessibility snapshot of a window. Returns a structured tree with element refs " +
        "that can be used with windows_click, windows_type, etc. This is the primary tool for " +
        "understanding window contents - use it before interacting with elements.";

    public override object InputSchema => new
    {
        type = "object",
        properties = new
        {
            handle = new
            {
                type = "string",
                description = "Window handle from windows_launch or windows_list_windows. If omitted, uses the most recently launched window."
            }
        }
    };

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var handle = GetStringArgument(arguments, "handle");

        try
        {
            FlaUI.Core.AutomationElements.Window? window = null;

            if (!string.IsNullOrEmpty(handle))
            {
                // Fail fast when this app's UIA provider is blocked by a pending
                // pattern call (e.g. a click that opened a modal dialog) —
                // walking the UIA tree would hang until the global timeout.
                var processId = _sessionManager.GetWindowProcessId(handle);
                if (_invokeTracker.TryGetPending(processId, out var pending))
                {
                    return Task.FromResult(ErrorResult(PendingInvokeTracker.DescribeBlocked(pending)));
                }

                window = _sessionManager.GetWindow(handle);
                if (window == null)
                {
                    return Task.FromResult(ErrorResult($"Window not found: {handle}"));
                }
            }
            else
            {
                // Get the foreground window
                var desktop = _sessionManager.Automation.GetDesktop();
                var focusedElement = _sessionManager.Automation.FocusedElement();
                
                if (focusedElement != null)
                {
                    // Walk up to find the window
                    var current = focusedElement;
                    while (current != null)
                    {
                        if (current.Properties.ControlType.ValueOrDefault == FlaUI.Core.Definitions.ControlType.Window)
                        {
                            window = current.AsWindow();
                            break;
                        }
                        current = current.Parent;
                    }
                }

                if (window == null)
                {
                    return Task.FromResult(ErrorResult("No window specified and no focused window found. Use windows_list_windows to see available windows."));
                }

                // Register this window
                handle = _sessionManager.RegisterWindow(window);
            }

            var snapshot = _snapshotBuilder.BuildSnapshot(handle!, window);
            return Task.FromResult(TextResult(snapshot));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ErrorResult($"Failed to capture snapshot: {ex.Message}"));
        }
    }
}
