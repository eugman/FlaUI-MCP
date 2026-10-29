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

    [Fact]
    public void CoveredFallbackReturnsTheSelectionWhenItSettledAfterTheFirstRead()
    {
        var selection = ClickTool.PatternResult(new PatternCallResult(PatternCallOutcome.Completed), "Selected Node");
        var refusal = new InputRefusedException(new InputRefusal(RefusalCodes.ClickPointCovered, "covered"));

        var result = ClickTool.FallbackAfterSelect(selection, () => throw refusal, () => true);

        Assert.Same(selection, result);
    }

    [Fact]
    public void CoveredFallbackStillRefusesWhenSelectionCannotBeConfirmed()
    {
        var selection = ClickTool.PatternResult(new PatternCallResult(PatternCallOutcome.Completed), "Selected Node");
        var refusal = new InputRefusedException(new InputRefusal(RefusalCodes.ClickPointCovered, "covered"));

        var observed = Assert.Throws<InputRefusedException>(() =>
            ClickTool.FallbackAfterSelect(selection, () => throw refusal, () => false)
        );

        Assert.Same(refusal, observed);
    }

    [Fact]
    public void FailureAfterPointerDispatchIsNeverHiddenByLateSelection()
    {
        var selection = ClickTool.PatternResult(new PatternCallResult(PatternCallOutcome.Completed), "Selected Node");
        var failure = new InvalidOperationException("pointer was sent; provider failed afterwards");

        var observed = Assert.Throws<InvalidOperationException>(() =>
            ClickTool.FallbackAfterSelect(selection, () => throw failure, () => true)
        );

        Assert.Same(failure, observed);
    }

    [Fact]
    public void OtherInputRefusalIsNeverHiddenByLateSelection()
    {
        var selection = ClickTool.PatternResult(new PatternCallResult(PatternCallOutcome.Completed), "Selected Node");
        var refusal = new InputRefusedException(new InputRefusal(RefusalCodes.FocusRevoked, "focus changed"));

        var observed = Assert.Throws<InputRefusedException>(() =>
            ClickTool.FallbackAfterSelect(selection, () => throw refusal, () => true)
        );

        Assert.Same(refusal, observed);
    }
}
