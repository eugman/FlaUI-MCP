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

    private static McpToolResult Fallback(AutomationElement element, bool verifyFocus, string reads) =>
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

        var error = Assert.Throws<InvalidOperationException>(() => Fallback(field, verifyFocus: true, reads: ""));

        Assert.Contains("no input sent", error.Message);
        Assert.Equal(1, desktop.ElementFocusRequests);
        Assert.Equal(0, desktop.Dispatched);
    }

    [Fact]
    public void AFocusedControlIsClearedAndVerified()
    {
        var desktop = new FakeDesktop { KeyboardFocus = false };
        using var installed = desktop.Install();

        var result = Fallback(Element(), verifyFocus: true, reads: "");

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("Filled field with \"\"", result.Content[0].Text);
        Assert.Equal("verified", result.Outcome!.Verification);
        Assert.Equal(2, desktop.Dispatched);
    }

    [Fact]
    public void KeyboardFillVerifiesFocusUnlessTheCallerTurnsItOff()
    {
        using var installed = new FakeDesktop().Install();
        var tool = new FillTool(new ElementRegistry());

        Assert.True(tool.KeyboardVerifiesFocus(null));
        Assert.True(tool.KeyboardVerifiesFocus(JsonSerializer.SerializeToElement(new { @ref = "w1e1" })));
        Assert.False(tool.KeyboardVerifiesFocus(JsonSerializer.SerializeToElement(new { verifyFocus = false })));
    }

    [Fact]
    public void TheFillSchemaOffersVerifyFocus()
    {
        using var installed = new FakeDesktop().Install();
        var schema = JsonSerializer.SerializeToElement(new FillTool(new ElementRegistry()).GetDefinition().InputSchema);

        Assert.True(schema.GetProperty("properties").TryGetProperty("verifyFocus", out var verifyFocus));
        Assert.Contains("default true", verifyFocus.GetProperty("description").GetString());
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
}
