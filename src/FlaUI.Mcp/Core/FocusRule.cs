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

        if (rule == FocusRule.Exact)
        {
            return false;
        }

        var focused = desktop.FocusedElement(element);
        for (var depth = 0; focused != null && depth < 50; depth++)
        {
            if (desktop.SameElement(focused, element))
            {
                return true;
            }

            focused = desktop.ParentOf(focused);
        }

        return false;
    }
}
