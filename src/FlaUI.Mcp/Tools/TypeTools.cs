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

    public TypeTool(ElementRegistry elementRegistry, PendingInvokeTracker? invokeTracker = null, ProcessPolicy? processPolicy = null)
    {
        _elementRegistry = elementRegistry;
        _invokeTracker = invokeTracker ?? new PendingInvokeTracker();
        _processPolicy = processPolicy ?? ProcessPolicy.AllowAll;
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
                    return Task.FromResult(ErrorResult(PendingInvokeTracker.DescribeBlocked(pending)));
                }

                element.Focus();
                Thread.Sleep(50); // Small delay to ensure focus
            }
            else
            {
                // Ref-less input goes to whatever has keyboard focus, so verify
                // the foreground window belongs to an allowed app.
                var denied = _processPolicy.CheckForegroundWindowAllowed();
                if (denied != null)
                {
                    return Task.FromResult(ErrorResult(denied));
                }
            }

            // Type the text
            Keyboard.Type(text);

            if (submit)
            {
                Keyboard.Press(VirtualKeyShort.ENTER);
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
            return Task.FromResult(ErrorResult(PendingInvokeTracker.DescribeBlocked(pending)));
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
                    valuePattern.SetValue(value);
                    return Task.FromResult(TextResult($"Filled {elementName} with \"{value}\""));
                }
            }

            // Fall back to focus + select all + type
            element.Focus();
            Thread.Sleep(50);
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
            Thread.Sleep(50);
            Keyboard.Type(value);

            return Task.FromResult(TextResult($"Filled {elementName} with \"{value}\""));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ErrorResult($"Failed to fill {refId}: {ex.Message}"));
        }
    }
}
