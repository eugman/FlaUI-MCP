using System.Text.Json;
using FlaUI.Mcp.Core;
using Xunit;

/// <summary>
/// The focus rule each TE3 fill asks Core's windows_fill for (review of 442823a): the Scintilla editors accept focus
/// on the editor or inside it, every other field must hold focus itself; none is filled without a check. Pure.
/// </summary>
public sealed class FillFocusTests
{
    private static JsonElement Rule(Te3Page.Target target) =>
        JsonSerializer.SerializeToElement(Te3Page.FillArguments("w1e5", "x", target)).GetProperty("verifyFocus");

    [Fact]
    public void TheScintillaEditorsAcceptFocusOnTheEditorOrInsideIt()
    {
        Assert.Equal(FocusRules.SelfOrDescendantName, Rule(Te3Page.ExpressionInputTarget()).GetString());
        Assert.Equal(FocusRules.SelfOrDescendantName, Rule(Te3Page.QueryInputTarget()).GetString());
    }

    [Fact]
    public void EveryOtherFieldMustHoldFocusItself()
    {
        foreach (
            var target in new[] { Te3Page.SearchTarget(), Te3Page.PropertyInputTarget(), Te3Page.OpenFileNameTarget() }
        )
        {
            Assert.True(Rule(target).GetBoolean());
        }
    }
}
