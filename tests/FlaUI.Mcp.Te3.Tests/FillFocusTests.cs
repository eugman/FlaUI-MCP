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

    [Fact]
    public void KeysToAScintillaEditorAskForFocusOnItOrInsideItExplicitly()
    {
        // Core's send-keys default with a ref is the same rule today; the Scintilla sites say it, so a change of
        // Core's default cannot change them. Other sends keep Core's default.
        var query = JsonSerializer.SerializeToElement(
            Te3Page.KeysArguments("w1", "w1e5", "F5", Te3Page.QueryInputTarget())
        );
        var tree = JsonSerializer.SerializeToElement(
            Te3Page.KeysArguments("w1", "w1e6", "Home", Te3Page.TomExplorerTarget())
        );

        Assert.Equal(FocusRules.SelfOrDescendantName, query.GetProperty("verifyFocus").GetString());
        Assert.Equal("F5", query.GetProperty("chord").GetString());
        Assert.False(tree.TryGetProperty("verifyFocus", out _));
        Assert.Equal("w1e6", tree.GetProperty("ref").GetString());
    }
}
