using FlaUI.Core.Definitions;
using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// Expand, collapse and toggle read the pattern's state back: "Expanded X" only when it is expanded, and a
/// state that did not move is a failure (the call went out, so dispatch stays completed).
/// </summary>
public sealed class PatternStateProofTests
{
    [Theory]
    [InlineData("expand", ExpandCollapseState.Expanded, "Expanded Tables", "verified")]
    [InlineData("expand", ExpandCollapseState.PartiallyExpanded, "Expanded Tables", "verified")]
    [InlineData(
        "expand",
        ExpandCollapseState.Collapsed,
        "Sent expand to Tables, but it is still Collapsed",
        "mismatch"
    )]
    [InlineData("expand", ExpandCollapseState.LeafNode, "Sent expand to Tables, but it is still LeafNode", "mismatch")]
    [InlineData("collapse", ExpandCollapseState.Collapsed, "Collapsed Tables", "verified")]
    [InlineData(
        "collapse",
        ExpandCollapseState.Expanded,
        "Sent collapse to Tables, but it is still Expanded",
        "mismatch"
    )]
    public void ExpandAndCollapseAreJudgedByTheStateReadBack(
        string action,
        ExpandCollapseState state,
        string message,
        string verification
    ) => Assert.Equal((message, verification), ClickTool.ExpandVerdict("Tables", action, state));

    [Fact]
    public void AnUnreadableExpandStateIsUnverified() =>
        Assert.Equal(
            ("Sent expand request to Tables; its state could not be read", "unavailable"),
            ClickTool.ExpandVerdict("Tables", "expand", null)
        );

    [Theory]
    [InlineData(ToggleState.Off, ToggleState.On, "verified")]
    [InlineData(ToggleState.On, ToggleState.On, "mismatch")]
    [InlineData(null, ToggleState.On, "unavailable")]
    public void AToggleIsJudgedByTheStateBeforeAndAfter(ToggleState? before, ToggleState? after, string verification) =>
        Assert.Equal(
            (ClickTool.ToggleOutcome("Bold", before, after), verification),
            ClickTool.ToggleVerdict("Bold", before, after)
        );

    [Fact]
    public void AStateThatDidNotMoveIsAFailedReplyWithDispatchCompleted()
    {
        var result = ClickTool.CheckedPatternResult(
            new PatternCallResult(PatternCallOutcome.Completed),
            "Sent expand to Tables, but it is still Collapsed",
            "mismatch"
        );

        Assert.True(result.IsError);
        Assert.Equal("completed", result.Outcome!.Dispatch);
        Assert.Equal("mismatch", result.Outcome.Verification);
    }

    [Fact]
    public void AVerifiedStateIsAnOkReply()
    {
        var result = ClickTool.CheckedPatternResult(
            new PatternCallResult(PatternCallOutcome.Completed),
            "Expanded Tables",
            "verified"
        );

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("verified", result.Outcome!.Verification);
    }
}
