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

    public override object InputSchema => ToolSchema.For<Arguments>();

    /// <summary>The tool's arguments; its schema is generated from them.</summary>
    internal sealed record Arguments
    {
        [ToolDoc("Optional explicit window handle; otherwise uses the focused element.")]
        public string? Handle { get; init; }

        [ToolDoc("Require ref and verified keyboard focus on that exact control before typing (default false).")]
        public bool? VerifyFocus { get; init; }

        [ToolDoc("Element ref from windows_snapshot (e.g., 'w1e5'). If omitted, types to currently focused element.")]
        public string? Ref { get; init; }

        [ToolDoc("Text to type")]
        public required string Text { get; init; }

        [ToolDoc("Press Enter after typing (default: false)")]
        public bool? Submit { get; init; }
    }

    /// <summary>The typed refusal for arguments that cannot be typed, or null when they can.</summary>
    private static McpToolResult? InvalidArguments(string? text, string? refId, bool verifyFocus)
    {
        if (text == null)
        {
            return ErrorResult(RefusalCodes.InvalidArguments, "Missing required argument: text");
        }

        return verifyFocus && string.IsNullOrWhiteSpace(refId)
            ? ErrorResult(RefusalCodes.InvalidArguments, "verifyFocus requires an element ref; no input sent.")
            : null;
    }

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var text = GetStringArgument(arguments, "text");
        var refId = GetStringArgument(arguments, "ref");
        var verifyFocus = GetBoolArgument(arguments, "verifyFocus");
        if (InvalidArguments(text, refId, verifyFocus) is { } invalid)
        {
            return Task.FromResult(invalid);
        }

        var submit = GetBoolArgument(arguments, "submit", false);
        var lines = Lines(text);
        var sentLines = 0;
        var inputStarted = false;

        try
        {
            OperationContext.Check();
            var handle = GetStringArgument(arguments, "handle");
            if (CheckTarget(refId, handle) is { } refused)
            {
                return Task.FromResult(refused);
            }

            // Type the text
            using var input = new GuardedInput(
                InputTarget(refId, handle),
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

    private InputTarget InputTarget(string? refId, string? handle) =>
        refId != null ? _elementRegistry.InputForRef(refId)
        : handle != null ? _sessions!.GetInputTarget(handle)
        : GuardedInput.ForegroundTarget(_processPolicy);

    /// <summary>The refusal for a target that cannot take the text, or null; nothing is focused or sent.</summary>
    private McpToolResult? CheckTarget(string? refId, string? handle)
    {
        if (handle != null && _sessions == null)
        {
            return ErrorResult("Window-handle input requires a session manager.");
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
                return BlockedResult(pending);
            }
        }
        else if (handle != null)
        {
            // Validate the explicit target, then GuardedInput focuses and
            // verifies that exact window before sending any input.
            if (!_processPolicy.IsProcessAllowed(_sessions!.GetInputTarget(handle!).ProcessId))
            {
                return ErrorResult(_processPolicy.DescribeDenied("Target process"));
            }
        }

        return null;
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

    public override object InputSchema => ToolSchema.For<Arguments>();

    /// <summary>The tool's arguments; its schema is generated from them.</summary>
    internal sealed record Arguments
    {
        [ToolDoc("Optional window handle to validate against the element ref.")]
        public string? Handle { get; init; }

        [ToolDoc("Element ref from windows_snapshot (e.g., 'w1e5')")]
        public required string Ref { get; init; }

        [ToolDoc("Value to fill")]
        public required string Value { get; init; }

        [ToolSchemaFrom(nameof(VerifyFocusSchema))]
        public object? VerifyFocus { get; init; }
    }

    public static object VerifyFocusSchema =>
        FocusRules.Schema(
            "Keyboard fallback only: require verified keyboard focus before Ctrl+A, true for the exact control, \"self-or-descendant\" for it or any descendant. By default the exact control, or its Edit child when the ref is a ComboBox or Spinner, or a focused Edit inside it with no other value control in between; set false only for a control that never reports focus, and the keys then go wherever focus is."
        );

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var refId = GetStringArgument(arguments, "ref");
        var value = GetStringArgument(arguments, "value");

        if (string.IsNullOrEmpty(refId))
        {
            return Task.FromResult(ErrorResult(RefusalCodes.InvalidArguments, "Missing required argument: ref"));
        }

        if (value == null)
        {
            return Task.FromResult(ErrorResult(RefusalCodes.InvalidArguments, "Missing required argument: value"));
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
            element.Properties.ControlType.ValueOrDefault,
            independent: false
        );
    }

    /// <summary>Whether the keyboard fallback requires verified focus: true unless the caller turns it off.</summary>
    internal static FocusRule KeyboardVerifiesFocus(JsonElement? arguments) =>
        FocusRules.Parse(arguments, "ref", FocusRule.EditInComposite);

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
        TextObservation observation;
        try
        {
            observation = ControlText.WaitForExpected(
                value,
                read,
                () =>
                {
                    validate();
                    input.Verify();
                },
                () => Thread.Sleep(50)
            );
        }
        catch (InputRefusedException moved) when (moved.Code == RefusalCodes.TargetChanged)
        {
            // Every key already went out; only the read-back stopped. Say that, not just "no further input sent".
            throw ReadBackInterrupted(elementName, value);
        }

        return FillReply(elementName, value, observation, new ToolOutcome("completed", "returned"));
    }
}
