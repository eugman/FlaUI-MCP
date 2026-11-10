using System.Runtime.CompilerServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// windows_type focuses a ref but, by default, does not require that it took focus. Its reply named the ref as the
/// place the text went even when the control never had keyboard focus; it now says focus was not verified.
/// </summary>
public sealed class TypeReplyTests
{
    private static AutomationElement Element() =>
        (AutomationElement)RuntimeHelpers.GetUninitializedObject(typeof(AutomationElement));

    [Fact]
    public void ARefThatHoldsKeyboardFocusIsNamedAsTheTarget()
    {
        using var installed = new FakeDesktop().Install();

        Assert.Equal("Typed \"abc\" into w1e5", TypeTool.Typed("abc", "w1e5", false, TypeTool.HoldsFocus(Element())));
    }

    [Fact]
    public void ARefThatNeverTookFocusIsNotClaimedAsTheTarget()
    {
        var desktop = new FakeDesktop();
        var canvas = Element();
        desktop.IgnoreFocus(canvas);
        using var installed = desktop.Install();

        Assert.Equal(
            "Typed and submitted \"abc\"; focus on w1e5 was not verified, so the text went wherever keyboard focus was. "
                + "Observe before continuing.",
            TypeTool.Typed("abc", "w1e5", true, TypeTool.HoldsFocus(canvas))
        );
    }

    [Fact]
    public void AFocusReadThatFailsIsNotVerifiedFocus()
    {
        var desktop = new FakeDesktop { FailKeyboardFocusAfterRequest = true };
        var element = Element();
        desktop.FocusElement(element);
        using var installed = desktop.Install();

        Assert.False(TypeTool.HoldsFocus(element));
    }

    [Fact]
    public void TypingWithoutARefNamesTheFocusedElement()
    {
        Assert.Equal(
            "Typed \"abc\" into focused element",
            TypeTool.Typed("abc", null, false, TypeTool.HoldsFocus(null))
        );
    }
}
