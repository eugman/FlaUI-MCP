using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Mcp.Core;

namespace FlaUI.Mcp.Tools;

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
        "Click an element by its ref (from windows_snapshot). Prefers Invoke pattern for reliability (tree and list rows: select), "
        + "falls back to mouse click if needed. Menu items and controls whose name ends in \"...\" get a physical click "
        + "by default, because an Invoke that opens a menu or dialog blocks UI Automation until it closes; pass physical=false to force Invoke. "
        + "If the click opens a modal dialog, returns immediately with the dialog title instead of waiting for the dialog to close. "
        + "A right-click after selecting a tree or list row can start inline rename; open context menus with windows_send_keys Shift+F10 or Apps.";

    public override object InputSchema =>
        new
        {
            type = "object",
            properties = new
            {
                handle = new
                {
                    type = "string",
                    description = "Optional window handle; when supplied, the ref must belong to this window.",
                },
                physical = new
                {
                    type = "boolean",
                    description = "Use a physical mouse click with foreground and native-window hit checks. Defaults to true for menu items and names ending in \"...\", otherwise false. These checks do not prove which UIA child receives the click; a container's clickable point may hit a child control.",
                },
                @ref = new { type = "string", description = "Element ref from windows_snapshot (e.g., 'w1e5')" },
                button = new
                {
                    type = "string",
                    @enum = new[] { "left", "right", "middle" },
                    description = "Mouse button to click (default: left)",
                },
                doubleClick = new { type = "boolean", description = "Whether to double-click (default: false)" },
                action = new
                {
                    type = "string",
                    @enum = new[] { "invoke", "select", "expand", "collapse", "toggle", "click" },
                    description = "Pattern to use instead of the default choice. Tree and list rows default to select, because Invoke on a tree row toggles its expansion; expand/collapse open or close a tree node without selecting it; click forces a mouse click.",
                },
            },
            required = new[] { "ref" },
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
        bool? requestedPhysical =
            arguments is { ValueKind: JsonValueKind.Object } a && a.TryGetProperty("physical", out _)
                ? GetBoolArgument(arguments, "physical", false)
                : null;

        // Fail fast if this app's UIA provider is already blocked by an earlier call
        var processId = _elementRegistry.GetProcessIdForRef(refId);
        if (_invokeTracker.TryGetPending(processId, out var pending))
        {
            return Task.FromResult(BlockedResult(pending));
        }

        try
        {
            OperationContext.Check();
            var handle = GetStringArgument(arguments, "handle");
            var element = _elementRegistry.ResolveRef(refId, handle);
            void Validate() => _elementRegistry.ValidateReference(refId, handle);
            OperationContext.Check();
            var elementName = element.Properties.Name.ValueOrDefault ?? refId;
            var physical =
                requestedPhysical ?? PrefersPhysical(element.Properties.ControlType.ValueOrDefault, elementName);

            var action = ChooseAction(
                GetStringArgument(arguments, "action"),
                physical || button != "left" || doubleClick,
                element.Properties.ControlType.ValueOrDefault,
                pattern => Supports(element, pattern)
            );
            var fallbackNote = "";
            if (action != null)
            {
                // Clicking a row that is already selected starts inline rename in TE3's tree, and the
                // next keystroke then renames the object; selecting it again is never needed.
                if (action == "select" && IsSelected(element))
                {
                    return Task.FromResult(TextResult($"{elementName} is already selected; nothing was clicked"));
                }

                var patternResult = RunPattern(element, action, processId, elementName, Validate);
                if (
                    action != "select"
                    || patternResult.IsError == true
                    || patternResult.Outcome?.IsPending == true
                    || Settled(() => IsSelected(element), 300)
                )
                {
                    return Task.FromResult(patternResult);
                }

                // Some providers (TE3's tree rows) accept Select() and change nothing; a real click selects.
                fallbackNote = " (Select changed nothing, so it was clicked instead)";
            }

            // Fall back to mouse click
            using var input = new GuardedInput(_elementRegistry.InputForRef(refId));

            var mouseButton = button switch
            {
                "right" => MouseButton.Right,
                "middle" => MouseButton.Middle,
                _ => MouseButton.Left,
            };

            if (doubleClick)
            {
                input.Click(element, mouseButton, true);
                return Task.FromResult(TextResult($"Double-clicked {elementName}"));
            }
            else
            {
                var opensDialog = physical && EndsWithEllipsis(elementName) && processId != 0;
                var before = opensDialog ? Win32Desktop.GetTopLevelWindows(processId) : [];
                input.Click(element, mouseButton);
                if (!opensDialog)
                {
                    return Task.FromResult(TextResult($"Clicked {elementName}{fallbackNote}"));
                }
                // A physical click returns before its dialog exists; report the window so agents don't list too early.
                for (var waited = 0; waited < 2000; waited += 100)
                {
                    if (NewWindowTitle(before, Win32Desktop.GetTopLevelWindows(processId)) is { } title)
                    {
                        return Task.FromResult(
                            TextResult(
                                $"Clicked {elementName}; window \"{title}\" opened. Get its handle from windows_list_windows."
                            )
                        );
                    }

                    Thread.Sleep(100);
                }

                return Task.FromResult(TextResult($"Clicked {elementName}; no new window appeared within 2 seconds."));
            }
        }
        catch (Exception ex)
        {
            IReadOnlyList<Win32WindowInfo> windows = [];
            try
            {
                var owner = _elementRegistry.GetProcessIdForRef(refId);
                if (owner != 0)
                {
                    windows = Win32Desktop.GetTopLevelWindows(owner);
                }
            }
            catch { }

            return Task.FromResult(
                ErrorResult($"Failed to click {refId}: {ClickFailure(ex.Message, ex.GetType().Name, windows)}")
            );
        }
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

    /// <summary>Polls a condition until it holds or the time runs out; providers update state asynchronously.</summary>
    private static bool Settled(Func<bool> condition, int timeoutMs)
    {
        for (var waited = 0; ; waited += 50)
        {
            if (condition())
            {
                return true;
            }

            if (waited >= timeoutMs)
            {
                return false;
            }

            Thread.Sleep(50);
        }
    }

    private static ToggleState? ReadToggle(AutomationElement element) =>
        element.Patterns.Toggle.TryGetPattern(out var toggle) && toggle.ToggleState.TryGetValue(out var state)
            ? state
            : null;

    /// <summary>What a toggle did, from the state read before and after it.</summary>
    internal static string ToggleOutcome(string name, ToggleState? before, ToggleState? after) =>
        (before, after) switch
        {
            (null, _) or (_, null) => $"Sent toggle request to {name}; its state could not be read",
            var (b, a) when b == a => $"Toggled {name}, but it is still {b}",
            var (b, a) => $"{name}: {b} -> {a}",
        };

    private static bool Supports(AutomationElement element, string action) =>
        action switch
        {
            "invoke" => element.Patterns.Invoke.IsSupported,
            "toggle" => element.Patterns.Toggle.IsSupported,
            "select" => element.Patterns.SelectionItem.IsSupported,
            "expand" or "collapse" => element.Patterns.ExpandCollapse.IsSupported,
            _ => false,
        };

    private McpToolResult RunPattern(
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
        // Pattern mutations share the input lease so they cannot interleave with physical input.
        using var lease = GuardedInput.AcquireLease();
        var result = ModalAwareInvoker.Execute(
            processId,
            $"{verb} on '{elementName}'",
            () => MutationGuard.Execute(validate, call),
            _invokeTracker
        );
        // Providers may update toggle state asynchronously, so wait briefly for it to move.
        ToggleState? toggleAfter = null;
        if (action == "toggle" && result.Outcome == PatternCallOutcome.Completed)
        {
            Settled(() => (toggleAfter = ReadToggle(element)) != toggleBefore, 500);
        }

        var message = action switch
        {
            "toggle" => result.Outcome == PatternCallOutcome.Completed
                ? ToggleOutcome(elementName, toggleBefore, toggleAfter)
                : $"Sent toggle request to {elementName}",
            "invoke" => $"Invoked {elementName}",
            "select" => $"Selected {elementName}",
            "expand" => $"Expanded {elementName}",
            _ => $"Collapsed {elementName}",
        };
        return PatternResult(result, message);
    }

    // An empty reason invites a blind retry; name the exception and any dialog that seems to block input.
    internal static string ClickFailure(string message, string exceptionType, IReadOnlyList<Win32WindowInfo> windows)
    {
        var reason = string.IsNullOrWhiteSpace(message) ? exceptionType : message;
        var modal = windows.Any(w => !w.IsEnabled && !w.IsToolWindow)
            ? windows.FirstOrDefault(w => w.IsEnabled && !w.IsToolWindow && !w.IsCloaked && w.Title.Length > 0)
            : null;
        return modal == null
            ? reason
            : $"{reason.TrimEnd('.')}. Window \"{modal.Title}\" appears to be a modal dialog blocking input; handle it first (see windows_list_windows).";
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

    internal static string? NewWindowTitle(IEnumerable<Win32WindowInfo> before, IEnumerable<Win32WindowInfo> after)
    {
        var known = before.Select(w => w.Hwnd).ToHashSet();
        return after.FirstOrDefault(w => !known.Contains(w.Hwnd) && w.Title.Length > 0)?.Title;
    }

    /// <summary>
    /// Map a modal-aware pattern call result to a tool result.
    /// </summary>
    private static McpToolResult PatternResult(PatternCallResult result, string completedMessage)
    {
        var response = result.Outcome switch
        {
            PatternCallOutcome.Completed => TextResult(completedMessage),
            PatternCallOutcome.ModalDetected => TextResult(
                $"{completedMessage}; a modal dialog \"{result.ModalTitle}\" opened and blocks UI Automation on this app until it closes. "
                    + "Find it with windows_list_windows, then use windows_screenshot or windows_send_keys (no ref) with its handle."
            ),
            _ => TextResult(
                $"{completedMessage}; the app's handler is still running. Check windows_operation_status or take a screenshot "
                    + "by handle before continuing; do not repeat the click."
            ),
        };
        return response with { Outcome = ToolOutcome.FromPattern(result) };
    }
}
