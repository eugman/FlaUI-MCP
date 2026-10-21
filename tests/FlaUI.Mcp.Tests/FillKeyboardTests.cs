using System.Runtime.CompilerServices;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// windows_fill's keyboard fallback sends Ctrl+A then Backspace or the new text. Without verified focus on the
/// control, those keys select and delete whatever has focus instead, so it refuses, and it reports "Filled"
/// only when the control reads back the requested value.
/// </summary>
[Collection(InputLeaseCollection.Name)]
public sealed class FillKeyboardTests
{
    private static AutomationElement Element() =>
        (AutomationElement)RuntimeHelpers.GetUninitializedObject(typeof(AutomationElement));

    private static McpToolResult Fallback(AutomationElement element, FocusRule verifyFocus, string reads) =>
        FillTool.FillByKeyboardFallback(
            FakeDesktop.Target,
            element,
            verifyFocus,
            "",
            "field",
            () => new(reads, "Value"),
            () => { }
        );

    [Fact]
    public void AControlThatIgnoresFocusIsRefusedBeforeCtrlAOrBackspace()
    {
        // Another control (a canvas, say) keeps keyboard focus; the field ignores the focus request.
        var desktop = new FakeDesktop();
        var field = Element();
        desktop.IgnoreFocus(field);
        using var installed = desktop.Install();

        var error = Assert.Throws<InvalidOperationException>(() =>
            Fallback(field, verifyFocus: FocusRule.SelfOrDescendant, reads: "")
        );

        Assert.Contains("no input sent", error.Message);
        Assert.Equal(1, desktop.ElementFocusRequests);
        Assert.Equal(0, desktop.Dispatched);
    }

    [Fact]
    public void AFocusedControlIsClearedAndVerified()
    {
        var desktop = new FakeDesktop { KeyboardFocus = false };
        using var installed = desktop.Install();

        var result = Fallback(Element(), verifyFocus: FocusRule.SelfOrDescendant, reads: "");

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("Filled field with \"\"", result.Content[0].Text);
        Assert.Equal("verified", result.Outcome!.Verification);
        Assert.Equal(2, desktop.Dispatched);
    }

    [Fact]
    public void KeyboardFillVerifiesFocusUnlessTheCallerTurnsItOff()
    {
        Assert.Equal(FocusRule.SelfOrDescendant, FillTool.KeyboardVerifiesFocus(null));
        Assert.Equal(
            FocusRule.SelfOrDescendant,
            FillTool.KeyboardVerifiesFocus(JsonSerializer.SerializeToElement(new { @ref = "w1e1" }))
        );
        Assert.Equal(
            FocusRule.None,
            FillTool.KeyboardVerifiesFocus(JsonSerializer.SerializeToElement(new { verifyFocus = false }))
        );
    }

    [Fact]
    public void TheFillSchemaOffersVerifyFocus()
    {
        using var installed = new FakeDesktop().Install();
        var schema = JsonSerializer.SerializeToElement(new FillTool(new ElementRegistry()).GetDefinition().InputSchema);

        Assert.True(schema.GetProperty("properties").TryGetProperty("verifyFocus", out var verifyFocus));
        Assert.Contains("Default \"self-or-descendant\"", verifyFocus.GetProperty("description").GetString());
    }

    [Fact]
    public void AMismatchedReadBackIsAFailureNotFilled()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        using var input = new GuardedInput(FakeDesktop.Target);

        var result = FillTool.FillByKeyboard(input, "new", "field", () => new("old", "Value"), () => { });

        Assert.True(result.IsError);
        Assert.DoesNotContain("Filled", result.Content[0].Text);
        Assert.Contains("it now reads \"old\"", result.Content[0].Text);
        Assert.Equal("mismatch", result.Outcome!.Verification);
        Assert.Equal("completed", result.Outcome.Dispatch);
    }

    [Fact]
    public void AnUnreadableControlIsNotReportedAsFilled()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        using var input = new GuardedInput(FakeDesktop.Target);

        var result = FillTool.FillByKeyboard(input, "new", "field", () => new(null, "password"), () => { });

        Assert.NotEqual(true, result.IsError);
        Assert.DoesNotContain("Filled", result.Content[0].Text);
        Assert.Contains("not verified", result.Content[0].Text);
        Assert.Equal("unavailable", result.Outcome!.Verification);
    }

    [Fact]
    public void FocusThatLeavesTheControlAfterCtrlAStopsBeforeBackspace()
    {
        var desktop = new FakeDesktop { KeyboardFocus = false, ElementFocusLostAfter = 1 };
        using var installed = desktop.Install();

        var error = Assert.Throws<InvalidOperationException>(() =>
            Fallback(Element(), verifyFocus: FocusRule.SelfOrDescendant, reads: "")
        );

        Assert.Contains("left the control after Ctrl+A", error.Message);
        Assert.Equal(1, desktop.Dispatched);
    }

    [Theory]
    [InlineData("w1e1", null, FocusRule.SelfOrDescendant)]
    [InlineData("w1e1", false, FocusRule.None)]
    [InlineData(null, null, FocusRule.None)]
    [InlineData(null, true, FocusRule.Exact)]
    public void SendKeysWithARefVerifiesFocusUnlessTurnedOff(string? refId, bool? requested, FocusRule rule) =>
        Assert.Equal(
            rule,
            FocusRules.Parse(
                requested is { } r
                    ? JsonSerializer.SerializeToElement(new { verifyFocus = r })
                    : JsonSerializer.SerializeToElement(new { }),
                refId
            )
        );
}
