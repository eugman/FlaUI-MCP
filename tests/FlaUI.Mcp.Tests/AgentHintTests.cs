using FlaUI.Core.Definitions;
using PlaywrightWindows.Mcp.Core;
using PlaywrightWindows.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

public sealed class AgentHintTests
{
    [Theory]
    [InlineData(ControlType.MenuItem, "Tools", true)]
    [InlineData(ControlType.Button, "Preferences...", true)]
    [InlineData(ControlType.Button, "Open…", true)]
    [InlineData(ControlType.Button, "OK", false)]
    [InlineData(ControlType.TreeItem, "Node20", false)]
    public void MenusAndEllipsisCommandsDefaultToPhysicalClicks(ControlType type, string name, bool expected)
        => Assert.Equal(expected, ClickTool.PrefersPhysical(type, name));

    [Fact]
    public void FocusDescriptionSaysUnknownWithoutAWindow()
        => Assert.Equal("focused element (control unknown)", Win32Desktop.DescribeFocus(0));

    [Fact]
    public void FindHintsOnlyWhenAFilteredSearchMatchesNothing()
    {
        var empty = new QueryResult([], 10, false, 0);
        Assert.Contains("value rather than name", FindTool.EmptyHint(new ElementSelector(Name: "Text Editors"), empty));
        Assert.Null(FindTool.EmptyHint(new ElementSelector(), empty));
        var one = new QueryResult([new ElementInfo("w1e1", "OK", "", "Button", "", true, false, null, [], 1)], 1, false, 0);
        Assert.Null(FindTool.EmptyHint(new ElementSelector(Name: "OK"), one));
    }
}
