using PlaywrightWindows.Mcp.Core;
using Xunit;

namespace FlaUI.Automation.Tests;

public sealed class LanguageChoicesTests
{
    private static ElementInfo Item(string name, bool selected = false, bool offscreen = false) =>
        new("ref", name, "", "ListItem", "", true, offscreen, null, [], 1, selected);
    private static ElementInfo[] Original() => [Item("English", true), Item("Deutsch (Beta)"),
        Item("Español (Preview)"), Item("Français (Beta)"), Item("中文(简体) (Preview)"), Item("日本語 (Beta)")];

    [Fact]
    public void AcceptsVisibleCurrentChoicesWithEnglishSelected() => Te3Page.VerifyLanguageChoices(Original());

    [Fact]
    public void RejectsMissingOrReorderedChoices()
    {
        Assert.Throws<InvalidOperationException>(() => Te3Page.VerifyLanguageChoices([]));
        Assert.Throws<InvalidOperationException>(() => Te3Page.VerifyLanguageChoices(Original().Reverse().ToArray()));
    }

    [Fact]
    public void RejectsHiddenOrChangedSelection()
    {
        var choices = Original();
        choices[0] = Item("English", false);
        Assert.Throws<InvalidOperationException>(() => Te3Page.VerifyLanguageChoices(choices));
        choices = Original();
        choices[1] = Item("Deutsch (Beta)", offscreen: true);
        Assert.Throws<InvalidOperationException>(() => Te3Page.VerifyLanguageChoices(choices));
    }

    [Fact]
    public void RejectsChangedLabelsExtraItemsAndAmbiguousSelection()
    {
        Assert.Throws<InvalidOperationException>(() => Te3Page.VerifyLanguageChoices([.. Original(), Item("Extra")]));
        var choices = Original();
        choices[1] = Item("Deutsch");
        Assert.Throws<InvalidOperationException>(() => Te3Page.VerifyLanguageChoices(choices));
        choices = Original();
        choices[1] = Item("Deutsch (Beta)", selected: true);
        Assert.Throws<InvalidOperationException>(() => Te3Page.VerifyLanguageChoices(choices));
        choices[0] = Item("English");
        Assert.Throws<InvalidOperationException>(() => Te3Page.VerifyLanguageChoices(choices));
        choices = Original().Select(item => item with { Selected = null }).ToArray();
        Assert.Throws<InvalidOperationException>(() => Te3Page.VerifyLanguageChoices(choices));
    }
}
