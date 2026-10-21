using System.Runtime.CompilerServices;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// Keys aimed at a ref need verified focus, but an editable ComboBox (and similar composites) gives keyboard
/// focus to its Edit child, not the ref itself. The default rule accepts focus on the ref or a descendant;
/// true still demands the exact control, false turns the check off.
/// </summary>
[Collection(InputLeaseCollection.Name)]
public sealed class FocusRuleTests
{
    private static AutomationElement Element() =>
        (AutomationElement)RuntimeHelpers.GetUninitializedObject(typeof(AutomationElement));

    private static JsonElement Args(object value) => JsonSerializer.SerializeToElement(value);

    [Fact]
    public void TheRuleIsReadFromTheArgument()
    {
        Assert.Equal(FocusRule.SelfOrDescendant, FocusRules.Parse(Args(new { @ref = "w1e1" }), "w1e1"));
        Assert.Equal(FocusRule.None, FocusRules.Parse(Args(new { }), null));
        Assert.Equal(FocusRule.Exact, FocusRules.Parse(Args(new { verifyFocus = true }), "w1e1"));
        Assert.Equal(FocusRule.None, FocusRules.Parse(Args(new { verifyFocus = false }), "w1e1"));
        Assert.Equal(
            FocusRule.SelfOrDescendant,
            FocusRules.Parse(Args(new { verifyFocus = "self-or-descendant" }), "w1e1")
        );
        Assert.Throws<ArgumentException>(() => FocusRules.Parse(Args(new { verifyFocus = "maybe" }), "w1e1"));
    }

    [Fact]
    public void FocusOnTheComboBoxsEditChildPassesTheDefaultRule()
    {
        var combo = Element();
        var edit = Element();
        var desktop = new FakeDesktop { FocusedOn = edit };
        desktop.IgnoreFocus(combo);
        desktop.Parents[edit] = combo;
        using var installed = desktop.Install();

        new GuardedInput(FakeDesktop.Target, combo, FocusRule.SelfOrDescendant).Dispose();

        Assert.Throws<InvalidOperationException>(() => new GuardedInput(FakeDesktop.Target, combo, FocusRule.Exact));
    }

    [Fact]
    public void FocusOutsideTheRefFailsTheDefaultRule()
    {
        var combo = Element();
        var canvas = Element();
        var desktop = new FakeDesktop { FocusedOn = canvas };
        desktop.IgnoreFocus(combo);
        using var installed = desktop.Install();

        var error = Assert.Throws<InvalidOperationException>(() =>
            new GuardedInput(FakeDesktop.Target, combo, FocusRule.SelfOrDescendant)
        );
        Assert.Contains("no input sent", error.Message);
        Assert.Equal(0, desktop.Dispatched);
    }

    [Fact]
    public void AFillOnAComboBoxAcceptsFocusOnItsEditChild()
    {
        var combo = Element();
        var edit = Element();
        var desktop = new FakeDesktop { FocusedOn = edit };
        desktop.IgnoreFocus(combo);
        desktop.Parents[edit] = combo;
        desktop.ControlTypes[combo] = FlaUI.Core.Definitions.ControlType.ComboBox;
        desktop.ControlTypes[edit] = FlaUI.Core.Definitions.ControlType.Button;
        using var installed = desktop.Install();

        new GuardedInput(FakeDesktop.Target, combo, FocusRule.EditInComposite).Dispose();
    }

    [Fact]
    public void AFillOnACanvasRefusesFocusOnAButtonInsideIt()
    {
        // Ctrl+A on a canvas whose toolbar button holds focus would select every object.
        var canvas = Element();
        var button = Element();
        var desktop = new FakeDesktop { FocusedOn = button };
        desktop.IgnoreFocus(canvas);
        desktop.Parents[button] = canvas;
        desktop.ControlTypes[button] = FlaUI.Core.Definitions.ControlType.Button;
        using var installed = desktop.Install();

        Assert.Throws<InvalidOperationException>(() =>
            new GuardedInput(FakeDesktop.Target, canvas, FocusRule.EditInComposite)
        );
        Assert.Equal(0, desktop.Dispatched);
    }

    [Fact]
    public void AFillAcceptsAFocusedEditOnlyWithNoOtherValueControlInBetween()
    {
        var field = Element();
        var middle = Element();
        var edit = Element();
        var desktop = new FakeDesktop { FocusedOn = edit };
        desktop.IgnoreFocus(field);
        desktop.Parents[edit] = middle;
        desktop.Parents[middle] = field;
        desktop.ControlTypes[edit] = FlaUI.Core.Definitions.ControlType.Edit;
        using var installed = desktop.Install();

        new GuardedInput(FakeDesktop.Target, field, FocusRule.EditInComposite).Dispose();

        desktop.ValueBearing.Add(middle);
        Assert.Throws<InvalidOperationException>(() =>
            new GuardedInput(FakeDesktop.Target, field, FocusRule.EditInComposite)
        );
    }
}
