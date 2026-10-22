using FlaUI.Mcp;
using FlaUI.Mcp.Core;
using Xunit;
using static CoreResults;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>Which failed results pause the grant; scripted results only.</summary>
public sealed class FailurePauseTests
{
    private static Te3Interaction Approved()
    {
        var interaction = new Te3Interaction(() => 0);
        interaction.Begin("batch", userApproved: true);
        return interaction;
    }

    [Fact]
    public void AFailureThatSaysNothingAboutItsInputPauses()
    {
        // A failed result with no outcome cannot show that nothing went out, so it counts as may-have-sent.
        using var interaction = Approved();

        FailurePause.After(interaction, Result(null), observation: false);

        Assert.Equal(
            Te3Interaction.PausedAfterFailure,
            Assert.Throws<InputRefusedException>(interaction.Require).Refusal.Code
        );
    }

    [Fact]
    public void AFailureThatSentNothingDoesNotPause()
    {
        using var interaction = Approved();

        FailurePause.After(
            interaction,
            Result(new ToolOutcome("not-dispatched", "not-tracked") { InputsSent = 0 }),
            observation: false
        );

        interaction.Require();
    }
}
