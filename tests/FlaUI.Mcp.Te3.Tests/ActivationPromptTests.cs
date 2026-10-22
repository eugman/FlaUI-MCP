using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>
/// The run's click prompt ends however the wait for the user's click ends (review of 663bdc8): a timeout, a
/// cancellation, or TE3 exiting or a second TE3 appearing during the wait revokes the grant with activation-denied
/// before the run captures and cleans up, so nothing is left asking. Scripted observer; nothing is hooked.
/// </summary>
public sealed class ActivationPromptTests
{
    private sealed class Observer : IInputObserver
    {
        public long Sequence => 0;

        public InputCounts Counts => new(0, 0, null);

        public void Dispose() { }
    }

    private static readonly System.Drawing.Rectangle Window = new(0, 0, 1920, 1080);

    public static TheoryData<Exception> WaitFailures =>
        new()
        {
            new TimeoutException(RunExecutor.ActivationTimeout),
            new OperationCanceledException(),
            new InvalidOperationException("TabularEditor3 exited."),
        };

    [Theory]
    [MemberData(nameof(WaitFailures))]
    public async Task AWaitThatFailsEndsThePromptWithActivationDenied(Exception failure)
    {
        using var interaction = new Te3Interaction(() => new Observer());
        interaction.Begin("run model-open", userApproved: true);

        var thrown = await Assert.ThrowsAnyAsync<Exception>(() =>
            RunExecutor.PromptForClick(interaction, () => throw failure, () => Window)
        );

        Assert.Same(failure, thrown);
        Assert.False(interaction.Active);
        Assert.Equal(
            Te3Interaction.ActivationDenied,
            Assert.Throws<InputRefusedException>(interaction.Require).Refusal.Code
        );
    }

    [Fact]
    public async Task ATe3WindowWithNoBoundsEndsThePromptToo()
    {
        using var interaction = new Te3Interaction(() => new Observer());
        interaction.Begin("run model-open", userApproved: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RunExecutor.PromptForClick(interaction, () => Task.CompletedTask, () => null)
        );

        Assert.Equal(
            Te3Interaction.ActivationDenied,
            Assert.Throws<InputRefusedException>(interaction.Require).Refusal.Code
        );
    }
}
