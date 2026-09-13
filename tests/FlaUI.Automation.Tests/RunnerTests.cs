using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace FlaUI.Automation.Tests;

public sealed class RunnerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "fla_runner_test_" + Guid.NewGuid().ToString("N"));
    public RunnerTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);
    [Fact] public void ConfigDefaultsAndPathsAreIndependentOfWorkingDirectory()
    {
        var path = Path.Combine(root, "config.json"); File.WriteAllText(path, """{"te3":"te3.exe","te":"te.exe"}""");
        var config = RunConfig.Read(path);
        Assert.Equal(1, config.Repeat); Assert.Equal("auto", config.FixtureMode);
        Assert.Equal(Path.Combine(root, "te3.exe"), config.Te3);
        Assert.Equal(8, RecipeCatalog.Select(config.Scenarios).Length);
        Assert.False(config.UseServer(RecipeCatalog.All.Single(r => r.Id == "file-load-three-cycles")));
    }
    [Fact] public void SelectionRejectsUnknownAndDuplicateRecipes()
    {
        Assert.Throws<ArgumentException>(() => RecipeCatalog.Select(["unknown"]));
        Assert.Throws<ArgumentException>(() => RecipeCatalog.Select(["model-open", "model-open"]));
        Assert.Throws<ArgumentException>(() => RecipeCatalog.Select([]));
    }
    [Theory]
    [InlineData("run")][InlineData("recover")][InlineData("fixture-reset")][InlineData("fixture-remove")]
    public async Task NoFocusRejectsMutationBeforeReadingFiles(string command)
        => Assert.Equal(2, await RunnerCommands.Execute([command, Path.Combine(root, "missing"), "--no-focus"]));
    [Fact] public async Task AbsenceMustSettleAndReappearanceRestartsClock()
    {
        var clock = Stopwatch.StartNew();
        await Te3Page.WaitForAbsence(() => clock.ElapsedMilliseconds is >= 100 and < 250, timeoutMs: 2000, settleMs: 300);
        Assert.True(clock.ElapsedMilliseconds >= 550);
    }
    [Fact] public async Task ProviderErrorIsNotAbsence()
        => await Assert.ThrowsAsync<IOException>(() => Te3Page.WaitForAbsence(() => throw new IOException("provider")));
    [Fact] public async Task PersistentPresenceTimesOut()
        => await Assert.ThrowsAsync<TimeoutException>(() => Te3Page.WaitForAbsence(() => true, timeoutMs: 100, settleMs: 50));
    [Fact] public void ModelHashIgnoresPropertyOrderButNotArrayOrder()
    {
        Assert.Equal(ModelStateVerification.Hash("""{"model":{"a":1,"b":2}}"""), ModelStateVerification.Hash("""{"model":{"b":2,"a":1}}"""));
        Assert.NotEqual(ModelStateVerification.Hash("""{"model":{"a":[1,2]}}"""), ModelStateVerification.Hash("""{"model":{"a":[2,1]}}"""));
    }
    [Fact] public void ModelHashRejectsDuplicateFields()
        => Assert.Throws<ArgumentException>(() => ModelStateVerification.Hash("""{"model":{"a":1,"a":2}}"""));
    [Theory]
    [InlineData("""{"truncated":true,"rows":[{"v":1}]}""")]
    [InlineData("""{"truncated":false,"rows":[{"v":"1"}]}""")]
    [InlineData("""{"truncated":false,"rows":[]}""")]
    public void QueryRequiresCompleteExactTypedResults(string response)
        => Assert.Throws<InvalidOperationException>(() => QueryAssertions.Verify(response, new(["v"], [[JsonSerializer.SerializeToElement(1)]])));
    [Fact] public void QueryAcceptsExactOrderedRows()
        => QueryAssertions.Verify("""{"truncated":false,"rows":[{"v":1},{"v":2}]}""", new(["v"], [[JsonSerializer.SerializeToElement(1)], [JsonSerializer.SerializeToElement(2)]]));
    [Fact] public void CompositionRejectsOverflowingCrop()
        => Assert.Throws<ArgumentException>(() => CaptureComposition.Validate(new(1, "x", "x", [new("x", "x", 100, 100, int.MaxValue, 0, 2, 2)])));
    [Fact] public void ArtifactPathsCannotEscapeRoot()
        => Assert.Throws<ArgumentException>(() => ArtifactFiles.ContainedPath("../outside.png", root));
    [Fact] public async Task PromotionCannotWriteOriginalDocs()
    {
        File.WriteAllText(Path.Combine(root, "test.png"), "preserve");
        var manifest = new RunManifest { Passed = true, DocsRoot = Path.Combine(root, "TabularEditorDocs"), Screenshots = new() { ["test"] = Path.Combine(root, "test.png") } };
        File.WriteAllText(Path.Combine(root, "manifest.json"), JsonSerializer.Serialize(manifest, RunConfig.Json));
        await Assert.ThrowsAsync<ArgumentException>(() => ArtifactFiles.Promote(root, "test", "test.png", false));
    }
}
