using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Mcp.Core;

namespace FlaUI.Mcp.Tools;

/// <summary>A pattern call's reply, its verification, and the state read back.</summary>
internal sealed record PatternVerdict(string Message, string? Verification, string? Observed);

/// <summary>The replies windows_click gives, kept apart from the code that clicks.</summary>
public partial class ClickTool
{
    private McpToolResult ScrollAndMouseClick(
        ClickRequest request,
        AutomationElement element,
        string elementName,
        bool physical,
        bool selectionFallback,
        Action validate
    )
    {
        // Scrolling the row into view changes the app, so it goes through the guards before the click's lease.
        var scrolled = ClickGeometry.ScrollIntoView(
            element,
            request.ProcessId,
            _invokeTracker,
            validate,
            _elementRegistry.InputForRef(request.RefId)
        );
        try
        {
            return MouseClick(request, element, elementName, physical, selectionFallback);
        }
        catch (Exception error) when (scrolled != null && GuardedInput.IsRefusal(error))
        {
            throw GuardedInput.ChangedFirst(error, scrolled);
        }
    }

    /// <summary>Do not use a cached popup row rectangle after Select; tree rows keep their guarded fallback.</summary>
    internal static McpToolResult AfterUnconfirmedSelect(
        ControlType type,
        string name,
        McpToolResult patternResult,
        Func<McpToolResult> treeFallback
    )
    {
        if (type != ControlType.ListItem)
        {
            return treeFallback();
        }

        return patternResult with
        {
            Content =
            [
                new()
                {
                    Type = "text",
                    Text =
                        $"Select was sent to {name}, but selection could not be confirmed. No pointer click was sent. "
                        + "Take a fresh snapshot and re-resolve the row before another action; do not retry blindly.",
                },
            ],
            Outcome = patternResult.Outcome?.WithProof(
                new Proof(Proof.Unavailable, null, "selected", "SelectionItem", Independent: false)
            ),
        };
    }

    internal static string SelectionClickOutcome(string name, bool? selected) =>
        selected switch
        {
            true => $"Selected {name} after fallback click (SelectionItem confirmed)",
            false =>
                $"Clicked {name} after Select changed nothing; selection was not observed. Inspect the current selection before continuing; do not repeat the click blindly.",
            _ =>
                $"Clicked {name} after Select changed nothing; selection could not be read. Inspect the current selection before continuing; do not repeat the click blindly.",
        };

    /// <summary>What a toggle did, from the state read before and after it.</summary>
    internal static string ToggleOutcome(string name, ToggleState? before, ToggleState? after) =>
        (before, after) switch
        {
            (null, _) or (_, null) => $"Sent toggle request to {name}; its state could not be read",
            var (b, a) when b == a => $"Toggled {name}, but it is still {b}",
            var (b, a) => $"{name}: {b} -> {a}",
        };

    /// <summary>A toggle's reply and its verification: verified when the state moved, mismatch when it did not.</summary>
    internal static PatternVerdict ToggleVerdict(string name, ToggleState? before, ToggleState? after) =>
        new(
            ToggleOutcome(name, before, after),
            (before, after) switch
            {
                (null, _) or (_, null) => "unavailable",
                var (b, a) when b == a => "mismatch",
                _ => "verified",
            },
            after?.ToString()
        );

    /// <summary>
    /// An expand or collapse reply from the state read back: verified when it reached the requested state (an
    /// expand also when partially expanded), mismatch when it did not, unavailable when it could not be read.
    /// </summary>
    internal static PatternVerdict ExpandVerdict(string name, string action, ExpandCollapseState? state)
    {
        if (state is not { } reached)
        {
            return new($"Sent {action} request to {name}; its state could not be read", "unavailable", null);
        }

        var done =
            action == "expand"
                ? reached is ExpandCollapseState.Expanded or ExpandCollapseState.PartiallyExpanded
                : reached == ExpandCollapseState.Collapsed;
        if (!done)
        {
            return new($"Sent {action} to {name}, but it is still {reached}", "mismatch", reached.ToString());
        }

        return new(action == "expand" ? $"Expanded {name}" : $"Collapsed {name}", "verified", reached.ToString());
    }

    /// <summary>
    /// The reply with its proof: the pattern state read back (from the same provider that acted, so not
    /// independent) against the state expected. Agreement is self-reported, not independent proof. A state
    /// that agreed but was followed by a dialog a moment later is unsettled: the dialog may undo or supersede it.
    /// </summary>
    internal static McpToolResult CheckedPatternResult(
        PatternCallResult result,
        PatternVerdict verdict,
        string source,
        string? expected
    )
    {
        var selfReported = verdict.Verification == Proof.Verified && result.ModalTitle == null;
        var message = selfReported
            ? verdict.Message
                + "; the same UI Automation provider reported this state, so it is not independently verified."
            : verdict.Message;
        var reply = CheckedPatternResult(result, message, verdict.Verification);
        var kind = verdict.Verification;
        if (verdict.Verification == Proof.Verified && result.ModalTitle != null)
        {
            kind = Proof.Unsettled;
        }
        else if (selfReported)
        {
            kind = Proof.SelfReported;
        }

        return kind == null
            ? reply
            : reply with
            {
                Outcome = reply.Outcome!.WithProof(
                    new Proof(kind, verdict.Observed, expected, source, Independent: false)
                ),
            };
    }

    /// <summary>
    /// A completed pattern call's reply with its read-back verification. A state that did not move is a
    /// failure; the call went out, so the dispatch stays completed.
    /// </summary>
    internal static McpToolResult CheckedPatternResult(PatternCallResult result, string message, string? verification)
    {
        var reply = PatternResult(result, message);
        if (verification == null)
        {
            return reply;
        }

        return reply with
        {
            IsError = verification == "mismatch" ? true : reply.IsError,
            Outcome = reply.Outcome! with { Verification = verification },
        };
    }

    // An empty reason invites a blind retry; name the exception and any dialog that seems to block input.
    internal static string ClickFailure(
        string message,
        string exceptionType,
        IReadOnlyList<Win32WindowInfo> windows,
        bool patternSent = false
    )
    {
        var reason = string.IsNullOrWhiteSpace(message) ? exceptionType : message;
        if (patternSent)
        {
            // The fallback click was refused, but the Select before it went out and is counted.
            reason = GuardedInput.NoteFocusChange(reason, "Select was sent");
        }

        // A dialog owned by a disabled window, never just the first enabled window while any is disabled.
        var modal = WindowBaseline.BlockingDialog(windows);
        return modal == null
            ? reason
            : $"{reason.TrimEnd('.')}. Window \"{modal.Title}\" appears to be a modal dialog blocking input; handle it first (see windows_list_windows).";
    }

    /// <summary>
    /// After Select went out, a refused fallback click's structured refusal must say so too, not only the text.
    /// </summary>
    internal static void NoteSelectSent(Exception error)
    {
        if (error is InputRefusedException refused)
        {
            InputRefusedException.Record(
                refused.Refusal with
                {
                    Message = GuardedInput.NoteFocusChange(refused.Refusal.Message, "Select was sent"),
                }
            );
        }
    }

    /// <summary>The title of a dialog the click opened: new, titled, and not a tooltip, menu or tool window.</summary>
    internal static string? NewWindowTitle(IEnumerable<Win32WindowInfo> before, IEnumerable<Win32WindowInfo> after) =>
        new WindowBaseline(before).Opened(after).FirstOrDefault(w => w.Title.Length > 0)?.Title;

    /// <summary>
    /// Map a modal-aware pattern call result to a tool result.
    /// </summary>
    internal static McpToolResult PatternResult(PatternCallResult result, string completedMessage)
    {
        var response = result.Outcome switch
        {
            PatternCallOutcome.Completed when result.ModalTitle != null => TextResult(
                $"{completedMessage}; then dialog \"{result.ModalTitle}\" opened. Find it with windows_list_windows and "
                    + "handle it before relying on the state read back."
            ),
            PatternCallOutcome.Completed => TextResult(completedMessage),
            PatternCallOutcome.ModalDetected => TextResult(
                $"{completedMessage}; a modal dialog \"{result.ModalTitle}\" opened and blocks UI Automation on this app until it closes. "
                    + "Find it with windows_list_windows, then use windows_screenshot or windows_send_keys (no ref) with its handle."
            ),
            _ when !result.Dispatched => TextResult(
                "Not sent yet: the request is still waiting on the app, whose UI Automation provider has not answered. "
                    + "Check windows_operation_status or take a screenshot by handle before continuing; do not repeat the click."
            ),
            _ => TextResult(
                $"{completedMessage}; the app's handler is still running. Check windows_operation_status or take a screenshot "
                    + "by handle before continuing; do not repeat the click."
            ),
        };
        if (result.WaitNote.Length > 0)
        {
            response = TextResult(response.Content[0].Text + result.WaitNote);
        }

        return response with
        {
            Outcome = ToolOutcome.FromPattern(result),
        };
    }
}
