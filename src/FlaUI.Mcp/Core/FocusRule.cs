using System.Text.Json;
using FlaUI.Core.AutomationElements;

namespace FlaUI.Mcp.Core;

/// <summary>Which keyboard focus guarded input requires on the intended control before it sends anything.</summary>
public enum FocusRule
{
    /// <summary>No focus check; input goes to whatever has focus in the target window.</summary>
    None,

    /// <summary>The control itself must report keyboard focus.</summary>
    Exact,

    /// <summary>
    /// The control or one of its descendants must hold focus: an editable ComboBox, a spin box or a date picker gives
    /// focus to its Edit child, so the exact rule would refuse it while keys still reach the control.
    /// </summary>
    SelfOrDescendant,

    /// <summary>
    /// The default for a fill, whose Ctrl+A selects everything wherever focus is: the control itself, or a descendant
    /// when the control is a ComboBox or Spinner (an editable composite whose Edit child takes focus), or when the
    /// focused descendant is an Edit with no other Value-bearing control between it and the ref (a date picker's or
    /// a custom field's text box). Focus on a button inside a canvas does not pass.
    /// </summary>
    EditInComposite,
}

public static class FocusRules
{
    public const string SelfOrDescendantName = "self-or-descendant";

    /// <summary>
    /// The rule a verifyFocus argument asks for: true is <see cref="FocusRule.Exact"/>, false is none, and
    /// "self-or-descendant" is <see cref="FocusRule.SelfOrDescendant"/>. Absent, a call with a ref gets
    /// <paramref name="whenRef"/> and a call without one gets none.
    /// </summary>
    public static FocusRule Parse(JsonElement? arguments, string? refId, FocusRule whenRef = FocusRule.SelfOrDescendant)
    {
        if (
            arguments is not { ValueKind: JsonValueKind.Object } args
            || !args.TryGetProperty("verifyFocus", out var value)
        )
        {
            return string.IsNullOrWhiteSpace(refId) ? FocusRule.None : whenRef;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => FocusRule.Exact,
            JsonValueKind.False => FocusRule.None,
            JsonValueKind.String when value.GetString() == SelfOrDescendantName => FocusRule.SelfOrDescendant,
            _ => throw new ArgumentException($"verifyFocus must be true, false or \"{SelfOrDescendantName}\"."),
        };
    }

    /// <summary>The verifyFocus schema property: true, false or "self-or-descendant".</summary>
    public static object Schema(string description) =>
        new
        {
            type = new[] { "boolean", "string" },
            @enum = new object[] { true, false, SelfOrDescendantName },
            description,
        };

    /// <summary>Whether <paramref name="element"/> holds keyboard focus under <paramref name="rule"/>.</summary>
    internal static bool Holds(FocusRule rule, AutomationElement element, InputDesktop desktop)
    {
        if (rule == FocusRule.None || desktop.HasKeyboardFocus(element))
        {
            return true;
        }

        if (rule == FocusRule.Exact || desktop.FocusedElement(element) is not { } focused)
        {
            return false;
        }

        if (PathUp(desktop, focused, element) is not { } between)
        {
            return false;
        }

        if (rule == FocusRule.SelfOrDescendant)
        {
            return true;
        }

        var type = desktop.ControlTypeOf(element);
        if (type is FlaUI.Core.Definitions.ControlType.ComboBox or FlaUI.Core.Definitions.ControlType.Spinner)
        {
            return true;
        }

        return desktop.ControlTypeOf(focused) == FlaUI.Core.Definitions.ControlType.Edit
            && !between.Any(desktop.HasValuePattern);
    }

    /// <summary>
    /// The elements strictly between <paramref name="focused"/> and its ancestor <paramref name="element"/>, or null
    /// when it is not under it.
    /// </summary>
    private static List<AutomationElement>? PathUp(
        InputDesktop desktop,
        AutomationElement focused,
        AutomationElement element
    )
    {
        var between = new List<AutomationElement>();
        var current = desktop.ParentOf(focused);
        for (var depth = 0; current != null && depth < 50; depth++)
        {
            if (desktop.SameElement(current, element))
            {
                return between;
            }

            between.Add(current);
            current = desktop.ParentOf(current);
        }

        return null;
    }
}
