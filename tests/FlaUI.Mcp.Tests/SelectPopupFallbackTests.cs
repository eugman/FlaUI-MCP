using FlaUI.Core.Definitions;
using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

public sealed class SelectPopupFallbackTests
{
    [Fact]
    public void UnconfirmedListSelectionDoesNotSendASecondPointerAction()
    {
        var clicks = 0;
        var selected = ClickTool.PatternResult(
            new PatternCallResult(PatternCallOutcome.Completed),
            "Selected Name only"
        );

        var result = ClickTool.AfterUnconfirmedSelect(
            ControlType.ListItem,
            "Name only",
            selected,
            () =>
            {
                clicks++;
                return selected;
            }
        );

        Assert.Equal(0, clicks);
        Assert.Contains("Select was sent", result.Content[0].Text);
        Assert.Contains("fresh snapshot", result.Content[0].Text);
        Assert.Equal("completed", result.Outcome!.Dispatch);
        Assert.Equal(Proof.Unavailable, result.Outcome.Proof!.Verdict);
    }

    [Fact]
    public void UnconfirmedTreeSelectionRetainsItsGuardedFallback()
    {
        var clicks = 0;
        var selected = ClickTool.PatternResult(new PatternCallResult(PatternCallOutcome.Completed), "Selected Node");

        var result = ClickTool.AfterUnconfirmedSelect(
            ControlType.TreeItem,
            "Node",
            selected,
            () =>
            {
                clicks++;
                return selected;
            }
        );

        Assert.Equal(1, clicks);
        Assert.Same(selected, result);
    }
}
