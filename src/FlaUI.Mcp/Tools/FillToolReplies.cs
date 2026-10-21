using FlaUI.Core.AutomationElements;
using FlaUI.Mcp.Core;

namespace FlaUI.Mcp.Tools;

/// <summary>The replies windows_fill gives and its pure replace step, kept apart from the code that fills.</summary>
public partial class FillTool
{
    /// <summary>
    /// Ctrl+A selected the control's text; Backspace or typing then replaces whatever is selected where focus is
    /// now, so a control that lost focus in between (a popup took it) stops the fill.
    /// </summary>
    internal static void RequireStillFocused((AutomationElement Element, FocusRule Rule)? focused)
    {
        if (focused is { } f && !FocusRules.Holds(f.Rule, f.Element, InputDesktop.Current))
        {
            throw new InvalidOperationException(
                "Keyboard focus left the control after Ctrl+A, so the selection was not replaced; no further input sent."
            );
        }
    }

    /// <summary>
    /// "Filled" only when the control reads back the requested value. A different value is a failure (the input
    /// went out, so dispatch stays completed); a control that cannot be read back is reported as not verified.
    /// </summary>
    internal static McpToolResult FillReply(
        string elementName,
        string value,
        TextObservation observed,
        ToolOutcome outcome,
        FlaUI.Core.Definitions.ControlType type = FlaUI.Core.Definitions.ControlType.Edit
    )
    {
        var verification = observed.Verify(value);
        var reply = verification switch
        {
            "verified" => TextResult($"Filled {elementName} with \"{value}\""),
            "mismatch" => ErrorResult($"Sent \"{value}\" to {elementName}" + FillMismatch(value, observed.Text, type)),
            _ => TextResult(
                $"Sent \"{value}\" to {elementName}; it could not be read back, so the fill is not verified."
            ),
        };
        return reply with { Outcome = outcome with { Verification = verification } };
    }

    internal static string FillMismatch(string expected, string? observed, FlaUI.Core.Definitions.ControlType type)
    {
        if (observed == null || observed == expected)
        {
            return "";
        }

        var hint =
            type == FlaUI.Core.Definitions.ControlType.ComboBox
                ? ". For a ComboBox, fill its Edit child instead."
                : ".";
        return $"; warning: it now reads \"{observed}\", so the value may not have applied{hint}";
    }

    internal static void ReplaceByKeyboard(string value, Action selectAll, Action deleteSelection, Action<string> type)
    {
        selectAll();
        // Typing an empty string sends no input and does not replace selection.
        // Each supplied operation retains the normal focus/cancellation guard.
        if (value.Length == 0)
        {
            deleteSelection();
        }
        else
        {
            type(value);
        }
    }
}
