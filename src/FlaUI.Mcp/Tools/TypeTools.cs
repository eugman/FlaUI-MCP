using System.Text.Json;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using PlaywrightWindows.Mcp.Core;

namespace PlaywrightWindows.Mcp.Tools;

/// <summary>
/// Type text into an element
/// </summary>
public class TypeTool : ToolBase
{
    private readonly ElementRegistry _elementRegistry;
    private readonly PendingInvokeTracker _invokeTracker;
    private readonly ProcessPolicy _processPolicy;
    private readonly SessionManager? _sessions;

    public TypeTool(ElementRegistry elementRegistry, PendingInvokeTracker? invokeTracker = null, ProcessPolicy? processPolicy = null, SessionManager? sessions = null)
    {
        _elementRegistry = elementRegistry;
        _invokeTracker = invokeTracker ?? new PendingInvokeTracker();
        _processPolicy = processPolicy ?? ProcessPolicy.AllowAll;
        _sessions = sessions;
    }

    public override string Name => "windows_type";

    public override string Description => 
        "Type text into an element. The element will be focused first. " +
        "Use this for typing without clearing existing content. Use windows_fill to replace content.";

    public override object InputSchema => new
    {
        type = "object",
        properties = new
        {
            handle = new { type = "string", description = "Optional explicit window handle; otherwise uses the focused element." },
            @ref = new
            {
                type = "string",
                description = "Element ref from windows_snapshot (e.g., 'w1e5'). If omitted, types to currently focused element."
            },
            text = new
            {
                type = "string",
                description = "Text to type"
            },
            submit = new
            {
                type = "boolean",
                description = "Press Enter after typing (default: false)"
            }
        },
        required = new[] { "text" }
    };

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var text = GetStringArgument(arguments, "text");
        if (text == null)
        {
            return Task.FromResult(ErrorResult("Missing required argument: text"));
        }

        var refId = GetStringArgument(arguments, "ref");
        var submit = GetBoolArgument(arguments, "submit", false);

        try
        {
            OperationContext.Check();
            var handle = GetStringArgument(arguments, "handle");
            if (refId != null && handle != null && _elementRegistry.WindowForRef(refId) != handle) throw new ArgumentException("Handle/ref mismatch.");
            // Focus element if ref provided
            if (!string.IsNullOrEmpty(refId))
            {
                var element = _elementRegistry.GetElement(refId);
                if (element == null)
                {
                    return Task.FromResult(ErrorResult($"Element not found: {refId}. Run windows_snapshot to refresh element refs."));
                }

                // Fail fast if this app's UIA provider is blocked (element.Focus() would hang).
                // Tip: calling windows_type without a ref types into the focused element
                // using pure keyboard input, which works even while the provider is blocked.
                if (_invokeTracker.TryGetPending(_elementRegistry.GetProcessIdForRef(refId), out var pending))
                {
                    return Task.FromResult(BlockedResult(pending));
                }

            }
            else if (handle != null)
            {
                // Validate the explicit target, then GuardedInput focuses and
                // verifies that exact window before sending any input.
                if (!_processPolicy.IsProcessAllowed(_sessions!.GetInputTarget(handle!).ProcessId))
                {
                    return Task.FromResult(ErrorResult(_processPolicy.DescribeDenied("Target process")));
                }
            }

            // Type the text
            using var input = new GuardedInput(refId != null ? _elementRegistry.InputForRef(refId) : handle != null ? _sessions!.GetInputTarget(handle) : GuardedInput.ForegroundTarget(_processPolicy), refId == null ? null : _elementRegistry.GetElement(refId));
            input.Type(text);

            if (submit)
            {
                input.Send(() => Keyboard.TypeSimultaneously(VirtualKeyShort.ENTER));
            }

            var target = string.IsNullOrEmpty(refId) ? "focused element" : refId;
            var action = submit ? "Typed and submitted" : "Typed";
            return Task.FromResult(TextResult($"{action} \"{text}\" into {target}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ErrorResult($"Failed to type: {ex.Message}"));
        }
    }
}

/// <summary>
/// Fill (clear and type) an element
/// </summary>
public class FillTool : ToolBase
{
    private readonly ElementRegistry _elementRegistry;
    private readonly PendingInvokeTracker _invokeTracker;

    public FillTool(ElementRegistry elementRegistry, PendingInvokeTracker? invokeTracker = null)
    {
        _elementRegistry = elementRegistry;
        _invokeTracker = invokeTracker ?? new PendingInvokeTracker();
    }

    public override string Name => "windows_fill";

    public override string Description => 
        "Clear and fill a text field with new value. Prefers Value pattern for reliability.";

    public override object InputSchema => new
    {
        type = "object",
        properties = new
        {
            handle = new { type = "string", description = "Optional window handle to validate against the element ref." },
            @ref = new
            {
                type = "string",
                description = "Element ref from windows_snapshot (e.g., 'w1e5')"
            },
            value = new
            {
                type = "string",
                description = "Value to fill"
            }
        },
        required = new[] { "ref", "value" }
    };

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var refId = GetStringArgument(arguments, "ref");
        var value = GetStringArgument(arguments, "value");

        if (string.IsNullOrEmpty(refId))
        {
            return Task.FromResult(ErrorResult("Missing required argument: ref"));
        }
        if (value == null)
        {
            return Task.FromResult(ErrorResult("Missing required argument: value"));
        }

        var element = _elementRegistry.GetElement(refId);
        if (element == null)
        {
            return Task.FromResult(ErrorResult($"Element not found: {refId}. Run windows_snapshot to refresh element refs."));
        }

        // Fail fast if this app's UIA provider is blocked by a pending pattern call
        if (_invokeTracker.TryGetPending(_elementRegistry.GetProcessIdForRef(refId), out var pending))
        {
                    return Task.FromResult(BlockedResult(pending));
        }

        try
        {
            var elementName = element.Properties.Name.ValueOrDefault ?? refId;

            // Try Value pattern first
            if (element.Patterns.Value.IsSupported)
            {
                var valuePattern = element.Patterns.Value.Pattern;
                if (!valuePattern.IsReadOnly.ValueOrDefault)
                {
                    OperationContext.Check();
                    var result = ModalAwareInvoker.Execute(_elementRegistry.GetProcessIdForRef(refId), "SetValue",
                        () => MutationGuard.Execute(() => _elementRegistry.ValidateReference(refId, GetStringArgument(arguments, "handle")),
                            () => valuePattern.SetValue(value)), _invokeTracker);
                    if (result.Outcome != PatternCallOutcome.Completed)
                        return Task.FromResult(ErrorResult(_invokeTracker.TryGetPending(_elementRegistry.GetProcessIdForRef(refId), out var p) ? PendingInvokeTracker.DescribeBlocked(p) : "SetValue outcome changed; inspect state before retrying.") with { Outcome = ToolOutcome.FromPattern(result) with { Dispatch = "failed" } });
                    return Task.FromResult(TextResult($"Filled {elementName} with \"{value}\"") with { Outcome = ToolOutcome.FromPattern(result) });
                }
            }

            // Fall back to focus + select all + type
            OperationContext.Check();
            _elementRegistry.ValidateReference(refId, GetStringArgument(arguments, "handle"));
            using var input = new GuardedInput(_elementRegistry.InputForRef(refId), element);
            ReplaceByKeyboard(value,
                () => input.Send(() => Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A)),
                () => input.Send(() => Keyboard.TypeSimultaneously(VirtualKeyShort.BACK)),
                input.Type);

            return Task.FromResult(TextResult($"Filled {elementName} with \"{value}\""));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ErrorResult($"Failed to fill {refId}: {ex.Message}"));
        }
    }

    internal static void ReplaceByKeyboard(string value, Action selectAll, Action deleteSelection, Action<string> type)
    {
        selectAll();
        // Typing an empty string sends no input and does not replace selection.
        // Each supplied operation retains the normal focus/cancellation guard.
        if (value.Length == 0) deleteSelection();
        else type(value);
    }
}
