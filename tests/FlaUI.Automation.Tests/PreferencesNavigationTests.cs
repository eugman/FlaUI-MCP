using PlaywrightWindows.Mcp.Core;
using Xunit;

namespace FlaUI.Automation.Tests;

public sealed class PreferencesNavigationTests
{
    private static ElementInfo Row(string value, bool selected = false) =>
        new("ref", "node", "", "TreeItem", "", true, false, value, [], 2, selected);

    [Fact]
    public void AcceptsCorrectGeneralAmongDuplicateLabels()
        => Te3Page.VerifyPreferencesFirstChild([Row("Text Editors"), Row("General"), Row("DAX Editor"), Row("General", true)], "DAX Editor", "General");

    [Fact]
    public void RejectsGeneralUnderAnotherParent()
        => Assert.Throws<InvalidOperationException>(() => Te3Page.VerifyPreferencesFirstChild(
            [Row("DAX Editor"), Row("General"), Row("File Formats"), Row("General", true)], "DAX Editor", "General"));

    [Fact]
    public void RejectsMissingOrAmbiguousObservation()
    {
        Assert.Throws<InvalidOperationException>(() => Te3Page.VerifyPreferencesFirstChild([], "DAX Editor", "General"));
        Assert.Throws<InvalidOperationException>(() => Te3Page.VerifyPreferencesFirstChild(
            [Row("DAX Editor"), Row("General", true), Row("Other", true)], "DAX Editor", "General"));
        Assert.Throws<InvalidOperationException>(() => Te3Page.VerifyPreferencesFirstChild(
            [Row("DAX Editor"), Row("General", true), Row("DAX Editor")], "DAX Editor", "General"));
    }
}
