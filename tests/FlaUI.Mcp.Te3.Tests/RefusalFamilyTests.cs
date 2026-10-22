using FlaUI.Mcp;
using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>
/// A failed Core call is one of Core's refusals (AutomationRefusal), not an InvalidOperationException, so a poll that
/// catches "not ready yet" cannot swallow it; its code is the hyphenated one, whatever spelling it came in.
/// </summary>
public sealed class RefusalFamilyTests
{
    private static CoreCallException Failed(ToolOutcome outcome) =>
        new("windows_click", new McpToolResult { Content = [new() { Text = "refused" }], Outcome = outcome });

    [Fact]
    public void AFailedCoreCallIsARefusalWithItsCodeAndInputs()
    {
        var error = Failed(
            new ToolOutcome("partial", "not-tracked")
            {
                InputsSent = 2,
                Failure = new InputRefusal("focus_revoked", "x"),
            }
        );

        AutomationRefusal refusal = error;
        Assert.False((Exception)error is InvalidOperationException);
        Assert.Equal("focus-revoked", refusal.Code);
        Assert.Equal(2, refusal.InputsSent);
        Assert.True(refusal.ActionDispatched);
        Assert.Equal(RefusalDisposition.Partial, refusal.Disposition);
    }

    [Fact]
    public void ACallThatMayStillGoOutIsUncertainAndOneThatSentNothingIsNoInput()
    {
        Assert.Equal(
            RefusalDisposition.Uncertain,
            Failed(new ToolOutcome("timed_out_pending", "pending") { InputsSent = 0 }).Disposition
        );
        var none = Failed(new ToolOutcome("not-dispatched", "not-tracked") { InputsSent = 0 });
        Assert.Equal(RefusalDisposition.NoInput, none.Disposition);
        Assert.Equal("tool-failed", none.Code);
    }

    [Theory]
    [InlineData("activation_denied", "activation-denied")]
    [InlineData("approval_required", "approval-required")]
    [InlineData("paused-after-failure", "paused-after-failure")]
    public void AnUnderscoredBlockerCodeIsReadAsTheHyphenatedOne(string given, string blocker)
    {
        var error = Failed(new ToolOutcome("not-dispatched", "not-tracked") { Failure = new InputRefusal(given, "x") });

        Assert.Equal(blocker, CoreFailure.Blocker(error));
    }

    [Fact]
    public void AnyOtherRefusalStillReportsItsCodeAndThatNothingWentOut()
    {
        var outcome = CoreFailure.Outcome(new ElementNotFoundException("No element matched"));

        Assert.Equal("not-dispatched", outcome?.Dispatch);
        Assert.Equal(RefusalCodes.ElementNotFound, outcome?.Failure?.Code);
    }
}
