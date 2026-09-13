using Xunit;
namespace FlaUI.Automation.Tests;

public sealed class RecoveryTests
{
    [Fact] public async Task TimedOutCliMustConfirmChildExit()
    {
        var steps = new List<string>();
        await CliRunner.ConfirmStopped(() => steps.Add("kill"), _ => { steps.Add("exit"); return Task.CompletedTask; });
        Assert.Equal(new[] { "kill", "exit" }, steps);
    }
    [Fact] public async Task UnconfirmedCliExitBlocksContinuation()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => CliRunner.ConfirmStopped(() => { },
            _ => throw new OperationCanceledException()));
        Assert.Contains("unconfirmed", error.Message);
    }
    [Theory]
    [InlineData("none", false, "close,reset,restore,save")]
    [InlineData("close", true, "close,save")]
    [InlineData("reset", true, "close,reset,restore,save")]
    [InlineData("restore", true, "close,reset,restore,save")]
    public async Task CleanupOrdersShutdownResetAndPreferences(string failure, bool needsRecovery, string sequence)
    {
        var calls = new List<string>();
        var manifest = new RunManifest { Passed = true, SettingsRestored = false };
        void Step(string name) { calls.Add(name); if (failure == name) throw new IOException("injected"); }
        var result = await RecoveryCoordinator.Complete(manifest,
            () => { Step("close"); return Task.CompletedTask; },
            () => { Step("reset"); return Task.CompletedTask; }, () => Step("restore"), () => Step("save"));
        Assert.Equal(sequence, string.Join(",", calls)); Assert.Equal(needsRecovery, result.NeedsRecovery);
        Assert.Equal(failure is "none" or "reset", manifest.SettingsRestored);
    }
    [Fact] public async Task RecoveryDoesNotTurnFailedRunIntoPassingRun()
    {
        var manifest = new RunManifest { Passed = false, Error = "failed", SettingsRestored = false };
        var result = await RecoveryCoordinator.Complete(manifest, () => Task.CompletedTask, () => Task.CompletedTask, () => { }, () => { });
        Assert.False(result.NeedsRecovery); Assert.False(manifest.Passed); Assert.Equal("failed", manifest.Error);
    }
    [Fact] public async Task AlreadyRestoredPreferencesAreNotRewritten()
    {
        await RecoveryCoordinator.Complete(new() { SettingsRestored = true }, () => Task.CompletedTask, () => Task.CompletedTask,
            () => throw new Exception("must not restore"), () => { });
    }
}
