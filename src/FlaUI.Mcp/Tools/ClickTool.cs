using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Mcp.Core;

namespace FlaUI.Mcp.Tools;

/// <summary>
/// Click an element by ref
/// </summary>
public partial class ClickTool : ToolBase
{
    private readonly ElementRegistry _elementRegistry;
    private readonly PendingInvokeTracker _invokeTracker;

    public ClickTool(ElementRegistry elementRegistry, PendingInvokeTracker? invokeTracker = null)
    {
        _elementRegistry = elementRegistry;
        _invokeTracker = invokeTracker ?? new PendingInvokeTracker();
    }

    public override string Name => "windows_click";

    public override McpToolAnnotations Annotations => McpToolAnnotations.Input(destructive: true);

    public override string Description =>
        "Click an element by its ref (from windows_snapshot). Prefers Invoke pattern for reliability (tree and list rows: select), "
        + "falls back to mouse click if needed. Menu items and controls whose name ends in \"...\" get a physical click "
        + "by default, because an Invoke that opens a menu or dialog blocks UI Automation until it closes; pass physical=false to force Invoke. "
        + "If the click opens a modal dialog, returns immediately with the dialog title instead of waiting for the dialog to close. "
        + "A select fallback click reports whether SelectionItem confirms selection; an unconfirmed click must be inspected before continuing. "
        + "A right-click after selecting a tree or list row can start inline rename; open context menus with windows_send_keys Shift+F10 or Apps.";

    public override object InputSchema => ToolSchema.For<Arguments>();

    /// <summary>The tool's arguments; its schema is generated from them.</summary>
    internal sealed record Arguments
    {
        [ToolDoc("Optional window handle; when supplied, the ref must belong to this window.")]
        public string? Handle { get; init; }

        [ToolDoc(
            "Use a physical mouse click with foreground and native-window hit checks. Defaults to true for menu items and names ending in \"...\", otherwise false. These checks do not prove which UIA child receives the click; a container's clickable point may hit a child control."
        )]
        public bool? Physical { get; init; }

        [ToolDoc("Element ref from windows_snapshot (e.g., 'w1e5')")]
        public required string Ref { get; init; }

        [ToolEnum("left", "right", "middle")]
        [ToolDoc("Mouse button to click (default: left)")]
        public string? Button { get; init; }

        [ToolDoc("Whether to double-click (default: false)")]
        public bool? DoubleClick { get; init; }

        [ToolEnum("invoke", "select", "expand", "collapse", "toggle", "click")]
        [ToolDoc(
            "Pattern to use instead of the default choice. Tree and list rows default to select, because Invoke on a tree row toggles its expansion; expand/collapse open or close a tree node without selecting it; click forces a mouse click."
        )]
        public string? Action { get; init; }
    }

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var refId = GetStringArgument(arguments, "ref");
        if (string.IsNullOrEmpty(refId))
        {
            return Task.FromResult(ErrorResult(RefusalCodes.InvalidArguments, "Missing required argument: ref"));
        }

        var request = new ClickRequest(
            refId,
            null,
            GetStringArgument(arguments, "button") ?? "left",
            GetBoolArgument(arguments, "doubleClick", false),
            arguments is { ValueKind: JsonValueKind.Object } a && a.TryGetProperty("physical", out _)
                ? GetBoolArgument(arguments, "physical", false)
                : null,
            null,
            // Fail fast if this app's UIA provider is already blocked by an earlier call
            _elementRegistry.GetProcessIdForRef(refId)
        );
        if (_invokeTracker.TryGetPending(request.ProcessId, out var pending))
        {
            return Task.FromResult(BlockedResult(pending));
        }

        ClickRequest? resolved = null;
        try
        {
            // Read inside the try, as before, so a malformed argument is reported as a failed click.
            resolved = request with
            {
                Handle = GetStringArgument(arguments, "handle"),
                Action = GetStringArgument(arguments, "action"),
            };
            return Task.FromResult(Click(resolved));
        }
        catch (Exception ex)
        {
            if (resolved?.PatternSent == true)
            {
                NoteSelectSent(ex);
            }

            IReadOnlyList<Win32WindowInfo> windows = [];
            try
            {
                var owner = _elementRegistry.GetProcessIdForRef(refId);
                if (owner != 0)
                {
                    windows = InputDesktop.Current.TopLevelWindows(owner);
                }
            }
            catch { }

            return Task.FromResult(
                ErrorResult(
                    $"Failed to click {refId}: {ClickFailure(ex.Message, ex.GetType().Name, windows, resolved?.PatternSent == true)}"
                )
            );
        }
    }

    /// <summary>What one windows_click call asks for, read from its arguments.</summary>
    private sealed record ClickRequest(
        string RefId,
        string? Handle,
        string Button,
        bool DoubleClick,
        bool? RequestedPhysical,
        string? Action,
        int ProcessId
    )
    {
        /// <summary>Set once a pattern call went out, so a later refusal does not claim nothing was sent.</summary>
        public bool PatternSent { get; set; }
    }

    /// <summary>Resolve the ref, then use a pattern when one fits and a guarded mouse click otherwise.</summary>
    private McpToolResult Click(ClickRequest request)
    {
        OperationContext.Check();
        var element = _elementRegistry.ResolveRef(request.RefId, request.Handle);
        void Validate() => _elementRegistry.ValidateReference(request.RefId, request.Handle);
        OperationContext.Check();
        var elementName = element.Properties.Name.ValueOrDefault ?? request.RefId;
        var physical =
            request.RequestedPhysical ?? PrefersPhysical(element.Properties.ControlType.ValueOrDefault, elementName);

        var action = ChooseAction(
            request.Action,
            physical || request.Button != "left" || request.DoubleClick,
            element.Properties.ControlType.ValueOrDefault,
            pattern => Supports(element, pattern)
        );
        if (action != null)
        {
            // In some tree controls, clicking a row that is already selected starts inline rename, and the
            // next keystroke then renames the object; selecting it again is never needed.
            if (
                action == "select"
                && element.Properties.ControlType.ValueOrDefault == ControlType.TreeItem
                && IsSelected(element)
            )
            {
                return TextResult($"{elementName} is already selected; nothing was clicked");
            }

            var patternResult = RunPattern(element, action, request.ProcessId, elementName, Validate);
            if (action != "select" || patternResult.IsError == true || patternResult.Outcome?.IsPending == true)
            {
                return patternResult;
            }

            // A list popup can close or reflow as Select returns. Its old rectangle is unsafe for a
            // fallback click even when the provider has not yet reported IsSelected.
            var selected =
                element.Properties.ControlType.ValueOrDefault == ControlType.ListItem
                    ? ObserveSelection(() => IsSelected(element)) == true
                    : Settled(() => IsSelected(element), 300);
            if (selected)
            {
                return patternResult;
            }

            return AfterUnconfirmedSelect(
                element.Properties.ControlType.ValueOrDefault,
                elementName,
                patternResult,
                () =>
                {
                    // Some tree providers accept Select() and change nothing; a guarded click selects.
                    request.PatternSent = true;
                    return ScrollAndMouseClick(request, element, elementName, physical, true, Validate);
                }
            );
        }

        return ScrollAndMouseClick(request, element, elementName, physical, false, Validate);
    }

    /// <summary>Fall back to a guarded mouse click, and report a dialog it opens.</summary>
    private McpToolResult MouseClick(
        ClickRequest request,
        AutomationElement element,
        string elementName,
        bool physical,
        bool selectionFallback
    )
    {
        using var input = new GuardedInput(_elementRegistry.InputForRef(request.RefId));

        var mouseButton = request.Button switch
        {
            "right" => MouseButton.Right,
            "middle" => MouseButton.Middle,
            _ => MouseButton.Left,
        };

        if (request.DoubleClick)
        {
            input.Click(element, mouseButton, true);
            return TextResult($"Double-clicked {elementName}");
        }

        var processId = request.ProcessId;
        var opensDialog = physical && EndsWithEllipsis(elementName) && processId != 0;
        var before = opensDialog ? InputDesktop.Current.TopLevelWindows(processId) : [];
        input.Click(element, mouseButton);
        if (!opensDialog)
        {
            if (selectionFallback)
            {
                var selected = ObserveSelection(() => IsSelected(element));
                return TextResult(SelectionClickOutcome(elementName, selected));
            }

            return TextResult($"Clicked {elementName}");
        }
        // A physical click returns before its dialog exists; report the window so agents don't list too early.
        for (var waited = 0; waited < 2000; waited += 100)
        {
            if (NewWindowTitle(before, InputDesktop.Current.TopLevelWindows(processId)) is { } title)
            {
                return TextResult(
                    $"Clicked {elementName}; window \"{title}\" opened. Get its handle from windows_list_windows."
                );
            }

            Thread.Sleep(100);
        }

        return TextResult($"Clicked {elementName}; no new window appeared within 2 seconds.");
    }

    internal static readonly string[] PatternActions = ["invoke", "select", "expand", "collapse", "toggle"];

    /// <summary>
    /// The pattern a left click uses, or null for a mouse click. Tree and list rows select: their
    /// Invoke usually toggles expansion or opens the item, which is rarely what "click" meant.
    /// </summary>
    internal static string? ChooseAction(
        string? requested,
        bool mouseOnly,
        ControlType type,
        Func<string, bool> supports
    )
    {
        if (requested != null)
        {
            if (requested == "click")
            {
                return null;
            }

            if (!PatternActions.Contains(requested))
            {
                throw new ArgumentException("action must be invoke, select, expand, collapse, toggle or click");
            }

            return supports(requested)
                ? requested
                : throw new InvalidOperationException($"This control does not support '{requested}'.");
        }

        if (mouseOnly)
        {
            return null;
        }

        if (type is ControlType.TreeItem or ControlType.ListItem && supports("select"))
        {
            return "select";
        }

        return new[] { "invoke", "toggle", "select" }.FirstOrDefault(supports);
    }

    private static bool IsSelected(AutomationElement element) =>
        element.Patterns.SelectionItem.TryGetPattern(out var selection)
        && selection.IsSelected.TryGetValue(out var selected)
        && selected;

    internal static bool? ObserveSelection(Func<bool> isSelected)
    {
        try
        {
            return Settled(isSelected, 500);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Poll <paramref name="condition"/> every 50 ms until it holds or the time passes on the desktop's clock;
    /// providers update state asynchronously. The poll count is capped as well, so a clock that never moves (a fake
    /// held still) cannot keep it waiting.
    /// </summary>
    private static bool Settled(Func<bool> condition, int timeoutMs)
    {
        const int PollMs = 50;
        var desktop = InputDesktop.Current;
        var started = desktop.Time.GetTimestamp();
        var held = condition();
        for (
            var polls = 0;
            !held
                && polls < (timeoutMs + PollMs - 1) / PollMs
                && desktop.Time.GetElapsedTime(started) < TimeSpan.FromMilliseconds(timeoutMs);
            polls++
        )
        {
            desktop.Pause(TimeSpan.FromMilliseconds(PollMs));
            held = condition();
        }

        return held;
    }

    private static ToggleState? ReadToggle(AutomationElement element) =>
        element.Patterns.Toggle.TryGetPattern(out var toggle) && toggle.ToggleState.TryGetValue(out var state)
            ? state
            : null;

    private static bool Supports(AutomationElement element, string action) =>
        action switch
        {
            "invoke" => element.Patterns.Invoke.IsSupported,
            "toggle" => element.Patterns.Toggle.IsSupported,
            "select" => element.Patterns.SelectionItem.IsSupported,
            "expand" or "collapse" => element.Patterns.ExpandCollapse.IsSupported,
            _ => false,
        };

    internal McpToolResult RunPattern(
        AutomationElement element,
        string action,
        int processId,
        string elementName,
        Action validate
    )
    {
        (string verb, Action call) = action switch
        {
            "invoke" => ("Invoke", (Action)(() => element.Patterns.Invoke.Pattern.Invoke())),
            "toggle" => ("Toggle", () => element.Patterns.Toggle.Pattern.Toggle()),
            "select" => ("Select", () => element.Patterns.SelectionItem.Pattern.Select()),
            "expand" => ("Expand", () => element.Patterns.ExpandCollapse.Pattern.Expand()),
            _ => ("Collapse", () => element.Patterns.ExpandCollapse.Pattern.Collapse()),
        };

        var toggleBefore = action == "toggle" ? ReadToggle(element) : null;
        // The guarded call takes the input lease, so it cannot interleave with physical input, and runs the
        // integrity check: Windows drops a pattern call into a higher-integrity app as it drops input.
        var result = GuardedDesktop.CallPattern(
            processId,
            $"{verb} on '{elementName}'",
            call,
            _invokeTracker,
            validate
        );
        if (result.Outcome != PatternCallOutcome.Completed)
        {
            return PatternResult(
                result,
                action == "toggle" ? $"Sent toggle request to {elementName}" : Done(action, elementName)
            );
        }

        // Read the state back: providers may update it asynchronously, so wait briefly for it to move.
        return action switch
        {
            "toggle" => CheckedPatternResult(
                result,
                ToggleVerdict(elementName, toggleBefore, SettledToggle(element, toggleBefore)),
                "Toggle",
                $"not {toggleBefore}"
            ),
            "expand" => CheckedPatternResult(
                result,
                ExpandVerdict(elementName, action, SettledExpand(element, ExpandCollapseState.Expanded)),
                "ExpandCollapse",
                nameof(ExpandCollapseState.Expanded)
            ),
            "collapse" => CheckedPatternResult(
                result,
                ExpandVerdict(elementName, action, SettledExpand(element, ExpandCollapseState.Collapsed)),
                "ExpandCollapse",
                nameof(ExpandCollapseState.Collapsed)
            ),
            _ => CheckedPatternResult(result, Done(action, elementName), null),
        };
    }

    private static string Done(string action, string elementName) =>
        action switch
        {
            "invoke" => $"Invoked {elementName}",
            "select" => $"Selected {elementName}",
            "expand" => $"Expanded {elementName}",
            _ => $"Collapsed {elementName}",
        };

    private static ToggleState? SettledToggle(AutomationElement element, ToggleState? before)
    {
        ToggleState? after = null;
        Settled(
            () =>
            {
                after = ReadToggle(element);
                return after != before;
            },
            500
        );
        return after;
    }

    private static ExpandCollapseState? ReadExpand(AutomationElement element) =>
        element.Patterns.ExpandCollapse.TryGetPattern(out var pattern)
        && pattern.ExpandCollapseState.TryGetValue(out var state)
            ? state
            : null;

    private static ExpandCollapseState? SettledExpand(AutomationElement element, ExpandCollapseState expected)
    {
        ExpandCollapseState? after = null;
        Settled(
            () =>
            {
                after = ReadExpand(element);
                return after == expected
                    || expected == ExpandCollapseState.Expanded && after == ExpandCollapseState.PartiallyExpanded;
            },
            500
        );
        return after;
    }

    /// <summary>
    /// Menus and "..." commands usually open a popup or modal. Invoke on them does not return
    /// until it closes, which blocks the provider the agent needs to inspect that popup.
    /// </summary>
    internal static bool PrefersPhysical(ControlType type, string? name) =>
        type == ControlType.MenuItem || EndsWithEllipsis(name);

    private static bool EndsWithEllipsis(string? name)
    {
        var trimmed = name?.TrimEnd() ?? "";
        return trimmed.EndsWith("...") || trimmed.EndsWith('…');
    }
}
