using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.Mcp.Core;

namespace FlaUI.Mcp.Tools;

/// <summary>
/// Type text into an element
/// </summary>
public class TypeTool : ToolBase
{
    private readonly ElementRegistry _elementRegistry;
    private readonly PendingInvokeTracker _invokeTracker;
    private readonly ProcessPolicy _processPolicy;
    private readonly SessionManager? _sessions;

    public TypeTool(
        ElementRegistry elementRegistry,
        PendingInvokeTracker? invokeTracker = null,
        ProcessPolicy? processPolicy = null,
        SessionManager? sessions = null
    )
    {
        _elementRegistry = elementRegistry;
        _invokeTracker = invokeTracker ?? new PendingInvokeTracker();
        _processPolicy = processPolicy ?? ProcessPolicy.AllowAll;
        _sessions = sessions;
    }

    public override string Name => "windows_type";

    public override McpToolAnnotations Annotations => McpToolAnnotations.Input(destructive: false);

    public override string Description =>
        "Type text into an element. The element will be focused first. Line breaks are sent as Enter. "
        + "Code editors can autocomplete typed text; use windows_paste for code. "
        + "Use this for typing without clearing existing content. Use windows_fill to replace content.";

    public override object InputSchema =>
        new
        {
            type = "object",
            additionalProperties = false,
            properties = new
            {
                handle = new
                {
                    type = "string",
                    description = "Optional explicit window handle; otherwise uses the focused element.",
                },
                verifyFocus = new
                {
                    type = "boolean",
                    description = "Require ref and verified keyboard focus on that exact control before typing (default false).",
                },
                @ref = new
                {
                    type = "string",
                    description = "Element ref from windows_snapshot (e.g., 'w1e5'). If omitted, types to currently focused element.",
                },
                text = new { type = "string", description = "Text to type" },
                submit = new { type = "boolean", description = "Press Enter after typing (default: false)" },
            },
            required = new[] { "text" },
        };

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var text = GetStringArgument(arguments, "text");
        if (text == null)
        {
            return Task.FromResult(ErrorResult("Missing required argument: text"));
        }

        var refId = GetStringArgument(arguments, "ref");
        var verifyFocus = GetBoolArgument(arguments, "verifyFocus");
        if (verifyFocus && string.IsNullOrWhiteSpace(refId))
        {
            return Task.FromResult(ErrorResult("verifyFocus requires an element ref; no input sent."));
        }

        var submit = GetBoolArgument(arguments, "submit", false);
        var lines = Lines(text);
        var sentLines = 0;
        var inputStarted = false;

        try
        {
            OperationContext.Check();
            var handle = GetStringArgument(arguments, "handle");
            if (handle != null && _sessions == null)
            {
                return Task.FromResult(ErrorResult("Window-handle input requires a session manager."));
            }
            // Focus element if ref provided
            if (!string.IsNullOrEmpty(refId))
            {
                _elementRegistry.ResolveRef(refId, handle);

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
            using var input = new GuardedInput(
                refId != null ? _elementRegistry.InputForRef(refId)
                    : handle != null ? _sessions!.GetInputTarget(handle)
                    : GuardedInput.ForegroundTarget(_processPolicy),
                refId == null ? null : _elementRegistry.GetElement(refId),
                verifyFocus
            );
            var target = string.IsNullOrEmpty(refId) ? "focused element" : refId;
            // Keyboard.Type maps '\n' to Ctrl+Enter, so line breaks are sent as explicit Enter presses.
            foreach (var line in lines)
            {
                inputStarted = true;
                if (sentLines > 0)
                {
                    input.Send(() => Keyboard.TypeSimultaneously(VirtualKeyShort.ENTER));
                }

                input.Type(line);
                sentLines++;
            }

            if (submit)
            {
                input.Send(() => Keyboard.TypeSimultaneously(VirtualKeyShort.ENTER));
            }

            var action = submit ? "Typed and submitted" : "Typed";
            return Task.FromResult(TextResult($"{action} \"{text}\" into {target}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(
                ErrorResult(
                    $"Failed to type: {ex.Message} Typed {sentLines} of {lines.Length} line(s)"
                        + (
                            inputStarted && sentLines < lines.Length
                                ? $"; line {sentLines + 1} may be partly typed."
                                : "."
                        )
                )
            );
        }
    }

    internal static string[] Lines(string text) => System.Text.RegularExpressions.Regex.Split(text, "\r\n|\r|\n");
}

/// <summary>
/// Fill (clear and type) an element
/// </summary>
public partial class FillTool : ToolBase
{
    private readonly ElementRegistry _elementRegistry;
    private readonly PendingInvokeTracker _invokeTracker;

    public FillTool(ElementRegistry elementRegistry, PendingInvokeTracker? invokeTracker = null)
    {
        _elementRegistry = elementRegistry;
        _invokeTracker = invokeTracker ?? new PendingInvokeTracker();
    }

    public override string Name => "windows_fill";

    /// <summary>The reply when SetValue has not returned within the grace period.</summary>
    internal static McpToolResult PendingSetValueResult(PatternCallResult result) =>
        TextResult(
            (
                result.Dispatched
                    ? "SetValue dispatched; the provider call is still pending. The final value is not verified. Inspect the dialog before continuing; do not repeat the fill."
                    : "SetValue has not been sent yet: it is still waiting on the app's UI Automation provider. Check windows_operation_status before continuing; do not repeat the fill."
            ) + result.WaitNote
        ) with
        {
            Outcome = ToolOutcome.FromPattern(result),
        };

    public override McpToolAnnotations Annotations => McpToolAnnotations.Input(destructive: true);

    public override string Description =>
        "Clear and fill a text field with new value. Prefers Value pattern for reliability. "
        + "Without a writable Value pattern it focuses the control and sends Ctrl+A, then Backspace or the new text; "
        + "it refuses before any key when the control does not take keyboard focus. Reports Filled only when the control reads back the value.";

    public override object InputSchema =>
        new
        {
            type = "object",
            additionalProperties = false,
            properties = new
            {
                handle = new
                {
                    type = "string",
                    description = "Optional window handle to validate against the element ref.",
                },
                @ref = new { type = "string", description = "Element ref from windows_snapshot (e.g., 'w1e5')" },
                value = new { type = "string", description = "Value to fill" },
                verifyFocus = FocusRules.Schema(
                    "Keyboard fallback only: require verified keyboard focus before Ctrl+A, true for the exact control, \"self-or-descendant\" for it or a descendant (an editable ComboBox focuses its Edit child). Default \"self-or-descendant\"; set false only for a control that never reports focus, and the keys then go wherever focus is."
                ),
            },
            required = new[] { "ref", "value" },
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

        // Fail fast if this app's UIA provider is blocked by a pending pattern call
        if (_invokeTracker.TryGetPending(_elementRegistry.GetProcessIdForRef(refId), out var pending))
        {
            return Task.FromResult(BlockedResult(pending));
        }

        try
        {
            var handle = GetStringArgument(arguments, "handle");
            var element = _elementRegistry.ResolveRef(refId, handle);
            var elementName = element.Properties.Name.ValueOrDefault ?? refId;

            // Try Value pattern first
            if (FillByValuePattern(element, refId, handle, value, elementName) is { } filled)
            {
                return Task.FromResult(filled);
            }

            // Fall back to focus + select all + type
            OperationContext.Check();
            _elementRegistry.ValidateReference(refId, handle);
            return Task.FromResult(
                FillByKeyboardFallback(
                    _elementRegistry.InputForRef(refId),
                    element,
                    KeyboardVerifiesFocus(arguments),
                    value,
                    elementName,
                    () =>
                    {
                        try
                        {
                            return ControlText.Read(element);
                        }
                        catch
                        {
                            return new(null, "unavailable");
                        }
                    },
                    () =>
                    {
                        OperationContext.Check();
                        _elementRegistry.ValidateReference(refId, handle);
                    }
                )
            );
        }
        catch (Exception ex)
        {
            return Task.FromResult(ErrorResult($"Failed to fill {refId}: {ex.Message}"));
        }
    }

    /// <summary>Fill through the Value pattern, or null when the control has no writable value.</summary>
    private McpToolResult? FillByValuePattern(
        AutomationElement element,
        string refId,
        string? handle,
        string value,
        string elementName
    )
    {
        if (!element.Patterns.Value.IsSupported)
        {
            return null;
        }

        var valuePattern = element.Patterns.Value.Pattern;
        if (valuePattern.IsReadOnly.ValueOrDefault)
        {
            return null;
        }

        OperationContext.Check();
        var result = SetValue(
            _elementRegistry.GetProcessIdForRef(refId),
            () => valuePattern.SetValue(value),
            () => _elementRegistry.ValidateReference(refId, handle)
        );
        if (result.Outcome != PatternCallOutcome.Completed)
        {
            return PendingSetValueResult(result);
        }
        // Some providers accept SetValue without applying it (e.g. a file dialog's ComboBox).
        string? observed = null;
        if (!element.Properties.IsPassword.ValueOrDefault)
        {
            observed = ControlText
                .WaitForExpected(
                    value,
                    () =>
                    {
                        try
                        {
                            return new(valuePattern.Value.ValueOrDefault, "Value");
                        }
                        catch
                        {
                            return new(null, "unavailable");
                        }
                    },
                    () =>
                    {
                        OperationContext.Check();
                        _elementRegistry.ValidateReference(refId, handle);
                    },
                    () => Thread.Sleep(50)
                )
                .Text;
        }

        return FillReply(
            elementName,
            value,
            new TextObservation(observed, "Value"),
            ToolOutcome.FromPattern(result),
            element.Properties.ControlType.ValueOrDefault
        );
    }

    /// <summary>Whether the keyboard fallback requires verified focus: true unless the caller turns it off.</summary>
    internal static FocusRule KeyboardVerifiesFocus(JsonElement? arguments) =>
        FocusRules.Parse(arguments, "ref", FocusRule.SelfOrDescendant);

    /// <summary>
    /// Focus the control and replace its text by keyboard. With <paramref name="verifyFocus"/>, a control that did
    /// not take keyboard focus is refused before Ctrl+A, which would otherwise select whatever has focus.
    /// </summary>
    internal static McpToolResult FillByKeyboardFallback(
        InputTarget target,
        AutomationElement element,
        FocusRule verifyFocus,
        string value,
        string elementName,
        Func<TextObservation> read,
        Action validate
    )
    {
        using var input = new GuardedInput(target, element, verifyFocus);
        return FillByKeyboard(
            input,
            value,
            elementName,
            read,
            validate,
            verifyFocus == FocusRule.None ? null : (element, verifyFocus)
        );
    }

    /// <summary>The SetValue call through the guarded pattern call: lease, integrity check, counted, modal-aware.</summary>
    internal PatternCallResult SetValue(int processId, Action setValue, Action validate) =>
        GuardedDesktop.CallPattern(processId, "SetValue", setValue, _invokeTracker, validate);

    /// <summary>Select all, replace by typing, then observe the text without retyping.</summary>
    internal static McpToolResult FillByKeyboard(
        GuardedInput input,
        string value,
        string elementName,
        Func<TextObservation> read,
        Action validate,
        (AutomationElement Element, FocusRule Rule)? focused = null
    )
    {
        ReplaceByKeyboard(
            value,
            () => input.Send(() => Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A)),
            () =>
            {
                RequireStillFocused(focused);
                input.Send(() => Keyboard.TypeSimultaneously(VirtualKeyShort.BACK));
            },
            text =>
            {
                RequireStillFocused(focused);
                input.Type(text);
            }
        );
        var observation = ControlText.WaitForExpected(
            value,
            read,
            () =>
            {
                validate();
                input.Verify();
            },
            () => Thread.Sleep(50)
        );
        return FillReply(elementName, value, observation, new ToolOutcome("completed", "returned"));
    }
}
