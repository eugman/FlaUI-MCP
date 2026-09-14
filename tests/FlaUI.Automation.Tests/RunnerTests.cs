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
    [InlineData("run")][InlineData("recover")]
    public async Task NoFocusRejectsMutationBeforeReadingFiles(string command)
        => Assert.Equal(2, await RunnerCommands.Execute([command, Path.Combine(root, "missing"), "--no-focus"]));
    [Fact] public async Task NoFocusRejectsRecoverEvenWithReadableBackup()
    {
        var backup = Path.Combine(root, "backup");
        Directory.CreateDirectory(backup);
        File.WriteAllText(Path.Combine(backup, "restore.json"), JsonSerializer.Serialize(new SettingsLease.BackupInfo(
            Path.Combine(root, "settings"), SettingsLease.Names.ToDictionary(n => n, n => (string?)null))));
        var error = new StringWriter();
        var original = Console.Error;
        Console.SetError(error);
        try { Assert.Equal(2, await RunnerCommands.Execute(["recover", backup, "--no-focus"])); }
        finally { Console.SetError(original); }
        Assert.Contains("--no-focus", error.ToString());
    }
    [Fact] public void ExitedProcessCountsAsClosedWithoutReadingIdentity()
    {
        using var exited = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit 0") { CreateNoWindow = true, UseShellExecute = false })!;
        exited.WaitForExit();
        var reads = 0;
        Assert.False(RunSafety.VerifyUnlessExited(exited, () => reads++));
        Assert.Equal(0, reads);
        using var running = Process.GetCurrentProcess();
        Assert.Throws<InvalidOperationException>(() => RunSafety.VerifyUnlessExited(running, () => throw new InvalidOperationException("mismatch")));
    }
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
    [Fact] public void RunnerLockExcludesConcurrentRunnerEntry()
    {
        using var first = RunExecutor.AcquireRunnerLock();
        Assert.Throws<IOException>(() =>
        {
            using var second = RunExecutor.AcquireRunnerLock();
        });
    }
    [Fact] public async Task CanceledRunnerEntryStopsBeforeDesktopOrSettingsWork()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => RunExecutor.Execute(
            new RunConfig(), new Recipe("test", "page", false, _ => Task.CompletedTask), cancellation.Token));
    }
    [Fact] public async Task BatchLockHolderEntersRecipeWithoutReacquiringLock()
    {
        using var held = RunExecutor.AcquireRunnerLock();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Reacquiring would throw IOException; cancellation proves the held lock was accepted.
        await Assert.ThrowsAsync<OperationCanceledException>(() => RunExecutor.Execute(
            new RunConfig(), new Recipe("test", "page", false, _ => Task.CompletedTask), cancellation.Token, held));
    }
    [Fact] public async Task RecoverRejectsBackupForAnotherSettingsDirectoryBeforeWriting()
    {
        var backup = Path.Combine(root, "backup");
        var target = Path.Combine(root, "other-settings");
        Directory.CreateDirectory(backup); Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "UiPreferences.json"), "{}");
        File.WriteAllText(Path.Combine(backup, "UiPreferences.json"), """{"Skin":"backup"}""");
        File.WriteAllText(Path.Combine(backup, "restore.json"), JsonSerializer.Serialize(new SettingsLease.BackupInfo(target,
            SettingsLease.Names.ToDictionary(n => n, n => (string?)null))));

        Assert.Equal(2, await RunnerCommands.Execute(["recover", backup]));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(target, "UiPreferences.json")));
    }
    [Fact] public async Task PromotionRejectsACheckpointChangedAfterCapture()
    {
        var png = Path.Combine(root, "checkpoint.png");
        File.WriteAllBytes(png, [1, 2, 3]);
        var docs = Path.Combine(root, "docs-copy");
        var manifest = new RunManifest { Passed = true, DocsRoot = docs, Screenshots = new() { ["checkpoint"] = png },
            ScreenshotHashes = new() { ["checkpoint"] = "00" } };
        File.WriteAllText(Path.Combine(root, "manifest.json"), JsonSerializer.Serialize(manifest, RunConfig.Json));
        await Assert.ThrowsAsync<IOException>(() => ArtifactFiles.Promote(root, "checkpoint", "out.png", false));
        Assert.False(Directory.Exists(docs));
    }
    [Fact] public async Task RecoverRequiresExactlyOneBackupDirectory()
        => Assert.Equal(2, await RunnerCommands.Execute(["recover"]));
    [Fact] public void CanceledStepBoundaryRecordsPendingMutationWithoutDispatching()
    {
        var manifest = new RunManifest();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => RunExecutor.ReportStep(manifest, cancellation.Token)("Click Preferences command"));
        Assert.Equal("Click Preferences command", manifest.CurrentStep);
    }
    [Fact] public void IndexWriteFailurePersistsFinalFailedManifestWithoutReplacingRunFailure()
    {
        var manifestPath = Path.Combine(root, "manifest.json");
        var manifest = new RunManifest { Passed = true, Error = "recipe failed", ErrorDetails = "original recipe failure" };

        RunExecutor.FinalizeArtifacts(manifest,
            () => File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, RunConfig.Json)),
            () => throw new IOException("index unavailable"));

        var persisted = JsonSerializer.Deserialize<RunManifest>(File.ReadAllText(manifestPath), RunConfig.Json)!;
        Assert.False(persisted.Passed);
        Assert.Equal("recipe failed", persisted.Error);
        Assert.Equal("original recipe failure", persisted.ErrorDetails);
        Assert.Contains("Writing results: index unavailable", persisted.CleanupError);
    }
    [Fact] public async Task PromotionCannotWriteOriginalDocs()
    {
        File.WriteAllText(Path.Combine(root, "test.png"), "preserve");
        var manifest = new RunManifest { Passed = true, DocsRoot = Path.Combine(root, "TabularEditorDocs"), Screenshots = new() { ["test"] = Path.Combine(root, "test.png") } };
        File.WriteAllText(Path.Combine(root, "manifest.json"), JsonSerializer.Serialize(manifest, RunConfig.Json));
        await Assert.ThrowsAsync<ArgumentException>(() => ArtifactFiles.Promote(root, "test", "test.png", false));
    }
}
