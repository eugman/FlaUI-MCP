using PlaywrightWindows.Mcp.Core;
using Xunit;

namespace FlaUI.Automation.Tests;

public sealed class StabilizationTests
{
    [Theory]
    [InlineData("offline", false, false)]
    [InlineData("offline", true, false)]
    [InlineData("auto", false, false)]
    [InlineData("auto", true, true)]
    [InlineData("fixed", false, true)]
    [InlineData("fixed", true, true)]
    public void FixtureModeDeterminesWhetherRecipeUsesServer(string mode, bool engine, bool expected)
    {
        var config = new RunConfig { FixtureMode = mode };
        var recipe = new Recipe("test", "page", engine, _ => Task.CompletedTask);
        Assert.Equal(expected, config.UseServer(recipe));
    }

    [Theory]
    [InlineData("fixture-reset")]
    [InlineData("fixture-remove")]
    public void OfflineConfigRejectsFixtureMutationBeforeOpeningExecutables(string command)
    {
        var error = Assert.Throws<ArgumentException>(() => RunnerCommands.RequireCommandMode(command, "offline"));
        Assert.Contains("forbids server fixture mutations", error.Message);
    }

    [Fact]
    public async Task DisappearingElementRestartsAbsenceSettlement()
    {
        var calls = 0;
        await Te3Page.WaitForAbsence(() =>
        {
            calls++;
            if (calls == 2) throw new System.Runtime.InteropServices.COMException("gone", unchecked((int)0x80040201));
            return false;
        }, timeoutMs: 2000, settleMs: 150);
        Assert.True(calls >= 5);
    }

    [Fact]
    public async Task PersistentUnavailableElementNeverPassesAbsence()
    {
        var error = await Assert.ThrowsAsync<TimeoutException>(() => Te3Page.WaitForAbsence(
            () => throw new FlaUI.Core.Exceptions.ElementNotAvailableException(), timeoutMs: 200, settleMs: 50));
        Assert.IsType<FlaUI.Core.Exceptions.ElementNotAvailableException>(error.InnerException);
    }

    [Fact]
    public async Task OtherComFailuresAreNotRetried()
    {
        var calls = 0;
        await Assert.ThrowsAsync<System.Runtime.InteropServices.COMException>(() => Te3Page.WaitForAbsence(() =>
        {
            calls++;
            throw new System.Runtime.InteropServices.COMException("denied", unchecked((int)0x80070005));
        }));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void BlankExceptionRetainsTypeAndFailingStep()
    {
        var manifest = new RunManifest { CurrentStep = "Wait for output to close" };
        RunExecutor.RecordFailure(manifest, new InvalidOperationException(""));
        Assert.Equal("InvalidOperationException", manifest.Error);
        Assert.Contains("InvalidOperationException", manifest.ErrorDetails);
        Assert.Equal("Wait for output to close", manifest.CurrentStep);
    }

    [Fact]
    public void FailureRetainsStackAndInnerException()
    {
        var manifest = new RunManifest();
        try { throw new InvalidOperationException("outer", new IOException("provider failed")); }
        catch (Exception error) { RunExecutor.RecordFailure(manifest, error); }
        Assert.Contains("provider failed", manifest.ErrorDetails);
        Assert.Contains(nameof(FailureRetainsStackAndInnerException), manifest.ErrorDetails);
    }

    [Theory]
    [InlineData("{\"truncated\":true,\"rows\":[{\"[Value]\":61}]}")]
    [InlineData("{\"rows\":[{\"[Value]\":61}]}")]
    [InlineData("{\"truncated\":false,\"rows\":[{\"[Value]\":\"61\"}]}")]
    [InlineData("{\"truncated\":false,\"rows\":[{\"[Value]\":62}]}")]
    [InlineData("{\"truncated\":false,\"rows\":[{\"a\":61,\"b\":61}]}")]
    public void ProductionScalarAssertionRejectsInexactResults(string response)
    {
        Assert.Throws<InvalidOperationException>(() => QueryAssertions.VerifyScalar(response, 61));
    }

    [Fact]
    public void ProductionScalarAssertionAcceptsNumericValue()
    {
        QueryAssertions.VerifyScalar("""{"truncated":false,"rows":[{"[Value]":61.0}]}""", 61);
    }

    [Fact]
    public void FailedRunIndexShowsEscapedDiagnosticsAndScreenshot()
    {
        var root = Path.Combine(Path.GetTempPath(), "fla_index_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllBytes(Path.Combine(root, "failure.png"), []);
            ArtifactFiles.WriteIndex(new RunManifest
            {
                Output = root, RunId = "test", CurrentStep = "<step>", ErrorDetails = "<script>failure</script>"
            });
            var html = File.ReadAllText(Path.Combine(root, "index.html"));
            Assert.Contains("Failed", html);
            Assert.Contains("&lt;step&gt;", html);
            Assert.Contains("&lt;script&gt;", html);
            Assert.Contains("failure.png", html);
            Assert.DoesNotContain("<script>", html);
        }
        finally { Directory.Delete(root, true); }
    }
}
