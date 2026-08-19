using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using PlaywrightWindows.Mcp.Core;

namespace PlaywrightWindows.Mcp.Tools;

/// <summary>
/// Click an element by ref
/// </summary>
public class ClickTool : ToolBase
{
    private readonly ElementRegistry _elementRegistry;
    private readonly PendingInvokeTracker _invokeTracker;

    public ClickTool(ElementRegistry elementRegistry, PendingInvokeTracker? invokeTracker = null)
    {
        _elementRegistry = elementRegistry;
        _invokeTracker = invokeTracker ?? new PendingInvokeTracker();
    }

    public override string Name => "windows_click";

    public override string Description =>
        "Click an element by its ref (from windows_snapshot). Prefers Invoke pattern for reliability, " +
        "falls back to mouse click if needed. If the click opens a modal dialog, returns immediately " +
        "with the dialog title instead of waiting for the dialog to close.";

    public override object InputSchema => new
    {
        type = "object",
        properties = new
        {
            @ref = new
            {
                type = "string",
                description = "Element ref from windows_snapshot (e.g., 'w1e5')"
            },
            button = new
            {
                type = "string",
                @enum = new[] { "left", "right", "middle" },
                description = "Mouse button to click (default: left)"
            },
            doubleClick = new
            {
                type = "boolean",
                description = "Whether to double-click (default: false)"
            }
        },
        required = new[] { "ref" }
    };

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var refId = GetStringArgument(arguments, "ref");
        if (string.IsNullOrEmpty(refId))
        {
            return Task.FromResult(ErrorResult("Missing required argument: ref"));
        }

        var button = GetStringArgument(arguments, "button") ?? "left";
        var doubleClick = GetBoolArgument(arguments, "doubleClick", false);

        var element = _elementRegistry.GetElement(refId);
        if (element == null)
        {
            return Task.FromResult(ErrorResult($"Element not found: {refId}. Run windows_snapshot to refresh element refs."));
        }

        // Fail fast if this app's UIA provider is already blocked by an earlier call
        var processId = _elementRegistry.GetProcessIdForRef(refId);
        if (_invokeTracker.TryGetPending(processId, out var pending))
        {
            return Task.FromResult(ErrorResult(PendingInvokeTracker.DescribeBlocked(pending)));
        }

        try
        {
            var elementName = element.Properties.Name.ValueOrDefault ?? refId;

            // Try Invoke pattern first (most reliable for buttons)
            if (button == "left" && !doubleClick && element.Patterns.Invoke.IsSupported)
            {
                var invokePattern = element.Patterns.Invoke.Pattern;
                var result = ModalAwareInvoker.Execute(
                    processId,
                    $"Invoke on '{elementName}'",
                    () => invokePattern.Invoke(),
                    _invokeTracker);
                return Task.FromResult(PatternResult(result, $"Invoked {elementName}"));
            }

            // Try Toggle pattern for checkboxes
            if (button == "left" && !doubleClick && element.Patterns.Toggle.IsSupported)
            {
                var togglePattern = element.Patterns.Toggle.Pattern;
                ToggleState? newState = null;
                var result = ModalAwareInvoker.Execute(
                    processId,
                    $"Toggle on '{elementName}'",
                    () =>
                    {
                        togglePattern.Toggle();
                        newState = togglePattern.ToggleState.ValueOrDefault;
                    },
                    _invokeTracker);
                return Task.FromResult(PatternResult(result, $"Toggled {elementName} to {newState}"));
            }

            // Try SelectionItem pattern for list items
            if (button == "left" && !doubleClick && element.Patterns.SelectionItem.IsSupported)
            {
                var selectionPattern = element.Patterns.SelectionItem.Pattern;
                var result = ModalAwareInvoker.Execute(
                    processId,
                    $"Select on '{elementName}'",
                    () => selectionPattern.Select(),
                    _invokeTracker);
                return Task.FromResult(PatternResult(result, $"Selected {elementName}"));
            }

            // Fall back to mouse click
            var clickPoint = element.GetClickablePoint();

            var mouseButton = button switch
            {
                "right" => MouseButton.Right,
                "middle" => MouseButton.Middle,
                _ => MouseButton.Left
            };

            if (doubleClick)
            {
                Mouse.DoubleClick(clickPoint, mouseButton);
                return Task.FromResult(TextResult($"Double-clicked {elementName}"));
            }
            else
            {
                Mouse.Click(clickPoint, mouseButton);
                return Task.FromResult(TextResult($"Clicked {elementName}"));
            }
        }
        catch (Exception ex)
        {
            return Task.FromResult(ErrorResult($"Failed to click {refId}: {ex.Message}"));
        }
    }

    /// <summary>
    /// Map a modal-aware pattern call result to a tool result.
    /// </summary>
    private static McpToolResult PatternResult(PatternCallResult result, string completedMessage)
    {
        return result.Outcome switch
        {
            PatternCallOutcome.Completed => TextResult(completedMessage),
            PatternCallOutcome.ModalDetected => TextResult(
                $"{completedMessage} — a modal dialog \"{result.ModalTitle}\" opened and is waiting for input. " +
                "Note: UIA-based tools (windows_snapshot, windows_get_text) on this app will block until the " +
                "dialog closes. Use windows_screenshot to see the dialog and windows_send_keys (without ref) " +
                "or coordinate clicks to interact with it."),
            _ => TextResult(
                $"{completedMessage} — the app's handler is still running in the background. " +
                "Take a windows_screenshot to check the app's state; UIA-based tools may block until it completes."),
        };
    }
}
