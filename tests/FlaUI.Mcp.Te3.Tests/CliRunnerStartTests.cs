using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>The TE CLI changes the model, so it starts through Core's guarded process start. Nothing starts.</summary>
public sealed class CliRunnerStartTests
{
    [Fact]
    public async Task TheCliStartsThroughCoresGuardedStart()
    {
        var desktop = new FakeDesktop();
        using (desktop.Install())
        {
            // The fake starts nothing and returns no process.
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new CliRunner().Run("fla-test-no-such-cli.exe", ["--version"])
            );
            Assert.Contains("did not start", error.Message);
        }

        Assert.Equal(1, desktop.StartRequests);
    }

    [Fact]
    public async Task TheOfflineSwitchRefusesARealStart()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CliRunner().Run("fla-test-no-such-cli.exe", ["--version"])
        );

        Assert.Contains("FLAUI_OFFLINE_TESTS", error.Message);
    }
}
