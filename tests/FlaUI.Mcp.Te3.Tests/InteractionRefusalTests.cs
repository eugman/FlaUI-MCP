using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>
/// A grant refusal carries a code the broker can read, instead of only text; nothing was sent or dispatched by it.
/// </summary>
public sealed class InteractionRefusalTests
{
    [Fact]
    public void InputFromTheUserRevokesWithFocusRevoked()
    {
        long input = 0;
        using var interaction = new Te3Interaction(() => input);
        interaction.Begin("batch", userApproved: true);
        input++;

        var error = Assert.Throws<InputRefusedException>(interaction.Require);

        Assert.Equal("focus-revoked", error.Refusal.Code);
        Assert.False(error.Refusal.ActionDispatched);
        Assert.Contains("keyboard or mouse input", error.Message);
    }

    [Fact]
    public void NoApprovedBatchIsApprovalRequired()
    {
        using var interaction = new Te3Interaction(() => 0);

        var error = Assert.Throws<InputRefusedException>(interaction.Require);

        Assert.Equal("approval-required", error.Refusal.Code);
    }

    [Fact]
    public void ARevokedGrantStaysFocusRevokedOnTheNextCall()
    {
        long input = 0;
        using var interaction = new Te3Interaction(() => input);
        interaction.Begin("batch", userApproved: true);
        input++;
        Assert.Throws<InputRefusedException>(interaction.Require);

        Assert.Equal("focus-revoked", Assert.Throws<InputRefusedException>(interaction.Require).Refusal.Code);
    }

    [Fact]
    public void TheToolOutcomeCarriesTheRefusalCodeAndTheInputsSent()
    {
        long input = 0;
        using var interaction = new Te3Interaction(() => input);
        interaction.Begin("batch", userApproved: true);
        input++;
        var error = Assert.Throws<InputRefusedException>(interaction.Require);

        var outcome = CoreFailure.Reply(error, inputsSent: 2);

        Assert.Equal("focus-revoked", outcome.Failure?.Code);
        Assert.Equal(2, outcome.InputsSent);
    }
}
