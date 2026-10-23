using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

public sealed class RunnerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "fla_runner_test_" + Guid.NewGuid().ToString("N"));

    public RunnerTests() => Directory.CreateDirectory(root);

    public void Dispose() => Directory.Delete(root, true);

    [Fact]
    public void ConfigDefaultsAndPathsAreIndependentOfWorkingDirectory()
    {
        var path = Path.Combine(root, "config.json");
        File.WriteAllText(path, """{"te3":"te3.exe","te":"te.exe"}""");
        var config = RunConfig.Read(path);
        Assert.Equal(1, config.Repeat);
        Assert.Equal("auto", config.FixtureMode);
        Assert.Equal(Path.Combine(root, "te3.exe"), config.Te3);
        Assert.Equal(8, RecipeCatalog.Select(config.Scenarios).Length);
        Assert.False(config.UseServer(RecipeCatalog.All.Single(r => r.Id == "model-open")));
    }

    [Fact]
    public void ConfigValidatesInputsNeededByEverySelectedFixtureMode()
    {
        var te3 = Path.Combine(root, "te3.exe");
        var te = Path.Combine(root, "te.exe");
        var baseline = Path.Combine(root, "baseline.bim");
        var fixture = Path.Combine(root, "fixture.csx");
        File.WriteAllText(te3, "");
        File.WriteAllText(te, "");
        File.WriteAllText(baseline, "");
        var config = new RunConfig
        {
            Te3 = te3,
            Te = te,
            OfflineBaseline = baseline,
            FixtureScript = fixture,
        };
        var offline = RecipeCatalog.All.Single(recipe => recipe.Id == "model-open");
        var engine = RecipeCatalog.All.Single(recipe => recipe.Id == "dax-query");

        config.ValidateFiles([offline]);
        Assert.Throws<FileNotFoundException>(() => config.ValidateFiles([offline, engine]));
        File.WriteAllText(fixture, "");
        config.ValidateFiles([offline, engine]);
    }

    [Fact]
    public void SelectionRejectsUnknownAndDuplicateRecipes()
    {
        Assert.Throws<ArgumentException>(() => RecipeCatalog.Select(["unknown"]));
        Assert.Throws<ArgumentException>(() => RecipeCatalog.Select(["model-open", "model-open"]));
        Assert.Throws<ArgumentException>(() => RecipeCatalog.Select([]));
    }

    [Fact]
    public void RunOptionsAcceptAScenarioListRepeatAndContinue()
    {
        var options = RunOptions.Parse("run", ["--scenario", "model-open, dax-query", "--continue", "--repeat", "2"]);
        Assert.NotNull(options.Scenarios);
        Assert.Equal(["model-open", "dax-query"], options.Scenarios);
        Assert.Equal(2, options.Repeat);
        Assert.True(options.Continue);
        Assert.Equal(new RunOptions(null, null, false), RunOptions.Parse("run", []));
    }

    [Theory]
    [InlineData("run", "--continue", "--continue")]
    [InlineData("run", "--scenario", null)]
    [InlineData("run", "--repeat", "0")]
    [InlineData("validate", "--continue", null)]
    public void RunOptionsRejectInvalidArguments(string command, string first, string? second) =>
        Assert.Throws<ArgumentException>(() => RunOptions.Parse(command, second == null ? [first] : [first, second]));

    [Fact]
    public void ContinueStopsWhenTheDesktopWasNotRestored()
    {
        Assert.True(RunOptions.SafeToContinue(new RunManifest { SettingsRestored = true }));
        Assert.False(RunOptions.SafeToContinue(new RunManifest { SettingsRestored = false }));
        Assert.False(RunOptions.SafeToContinue(new RunManifest { SettingsRestored = true, NeedsRecovery = true }));
        Assert.False(
            RunOptions.SafeToContinue(new RunManifest { SettingsRestored = true, CleanupError = "TE3 still running" })
        );
    }

    [Theory]
    [InlineData("run")]
    [InlineData("recover")]
    public async Task NoFocusRejectsMutationBeforeReadingFiles(string command) =>
        Assert.Equal(2, await RunnerCommands.Execute([command, Path.Combine(root, "missing"), "--no-focus"]));

    [Fact]
    public async Task NoFocusRejectsRecoverEvenWithReadableBackup()
    {
        var backup = Path.Combine(root, "backup");
        Directory.CreateDirectory(backup);
        File.WriteAllText(
            Path.Combine(backup, "restore.json"),
            JsonSerializer.Serialize(
                new SettingsLease.BackupInfo(
                    Path.Combine(root, "settings"),
                    SettingsLease.Names.ToDictionary(n => n, n => (string?)null)
                )
            )
        );
        var error = new StringWriter();
        var original = Console.Error;
        Console.SetError(error);
        try
        {
            Assert.Equal(2, await RunnerCommands.Execute(["recover", backup, "--no-focus"]));
        }
        finally
        {
            Console.SetError(original);
        }

        Assert.Contains("--no-focus", error.ToString());
    }

    [Fact]
    public void ExitedProcessCountsAsClosedWithoutReadingIdentity()
    {
        using var exited = Process.Start(
            new ProcessStartInfo("cmd.exe", "/c exit 0") { CreateNoWindow = true, UseShellExecute = false }
        )!;
        exited.WaitForExit();
        var reads = 0;
        Assert.False(RunSafety.VerifyUnlessExited(exited, () => reads++));
        Assert.Equal(0, reads);
        using var running = Process.GetCurrentProcess();
        Assert.Throws<InvalidOperationException>(() =>
            RunSafety.VerifyUnlessExited(running, () => throw new InvalidOperationException("mismatch"))
        );
    }

    [Fact]
    public async Task AbsenceMustSettleAndReappearanceRestartsClock()
    {
        // Absent at 0, present at 100 and 200, absent again from 300: settled 300 ms later, at 600.
        var clock = new FakeClock();
        await Absence.WaitFor(
            () => Task.FromResult(clock.Now.TotalMilliseconds is >= 100 and < 250),
            timeoutMs: 2000,
            settleMs: 300,
            clock.Elapsed,
            clock.Delay
        );
        Assert.Equal(600, clock.Now.TotalMilliseconds);
    }

    [Fact]
    public async Task ProviderErrorIsNotAbsence() =>
        await Assert.ThrowsAsync<IOException>(() => Te3Page.WaitForAbsence(() => throw new IOException("provider")));

    [Fact]
    public async Task PersistentPresenceTimesOut()
    {
        var clock = new FakeClock();
        await Assert.ThrowsAsync<TimeoutException>(() =>
            Absence.WaitFor(() => Task.FromResult(true), timeoutMs: 100, settleMs: 50, clock.Elapsed, clock.Delay)
        );
    }

    [Fact]
    public void ModelHashIgnoresPropertyOrderButNotArrayOrder()
    {
        Assert.Equal(
            ModelStateVerification.Hash("""{"model":{"a":1,"b":2}}"""),
            ModelStateVerification.Hash("""{"model":{"b":2,"a":1}}""")
        );
        Assert.NotEqual(
            ModelStateVerification.Hash("""{"model":{"a":[1,2]}}"""),
            ModelStateVerification.Hash("""{"model":{"a":[2,1]}}""")
        );
    }

    [Fact]
    public void ModelHashRejectsDuplicateFields() =>
        Assert.Throws<ArgumentException>(() => ModelStateVerification.Hash("""{"model":{"a":1,"a":2}}"""));

    [Theory]
    [InlineData("""{"truncated":true,"rows":[{"v":1}]}""")]
    [InlineData("""{"truncated":false,"rows":[{"v":"1"}]}""")]
    [InlineData("""{"truncated":false,"rows":[]}""")]
    public void QueryRequiresCompleteExactTypedResults(string response) =>
        Assert.Throws<InvalidOperationException>(() =>
            QueryAssertions.Verify(
                response,
                new(
                    ["v"],
                    [
                        [JsonSerializer.SerializeToElement(1)],
                    ]
                )
            )
        );

    [Fact]
    public void QueryAcceptsExactOrderedRows() =>
        QueryAssertions.Verify(
            """{"truncated":false,"rows":[{"v":1},{"v":2}]}""",
            new(
                ["v"],
                [
                    [JsonSerializer.SerializeToElement(1)],
                    [JsonSerializer.SerializeToElement(2)],
                ]
            )
        );

    [Fact]
    public void CompositionRejectsOverflowingCrop() =>
        Assert.Throws<ArgumentException>(() =>
            CaptureComposition.Validate(new(1, "x", "x", [new("x", "x", 100, 100, int.MaxValue, 0, 2, 2)]))
        );

    [Fact]
    public void ArtifactPathsCannotEscapeRoot() =>
        Assert.Throws<ArgumentException>(() => ArtifactFiles.ContainedPath("../outside.png", root));

    [Fact]
    public void RunnerLockExcludesConcurrentRunnerEntry()
    {
        // A lock of the test's own: the real one belongs to whatever runner is going on this machine.
        using var first = RunExecutor.AcquireRunnerLock(LockPath);
        Assert.Throws<IOException>(() =>
        {
            using var second = RunExecutor.AcquireRunnerLock(LockPath);
        });
    }

    [Fact]
    public void TheRealRunnerLockIsTheMachineWideOne() =>
        Assert.Equal(Path.Combine(Path.GetTempPath(), "fla_te3_automation.lock"), RunExecutor.RunnerLockPath);

    private string LockPath => Path.Combine(root, "runner.lock");

    [Fact]
    public async Task CanceledRunnerEntryStopsBeforeDesktopOrSettingsWork()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var grant = new Te3Interaction(() => 0);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            RunExecutor.Execute(
                new RunConfig { Output = root },
                new Recipe("test", "page", false, _ => Task.CompletedTask),
                grant,
                cancellation.Token,
                heldRunnerLock: null,
                new RunPaths(LockPath, Path.Combine(root, "settings"))
            )
        );
    }

    [Fact]
    public async Task BatchLockHolderEntersRecipeWithoutReacquiringLock()
    {
        using var held = RunExecutor.AcquireRunnerLock(LockPath);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var grant = new Te3Interaction(() => 0);

        // Reacquiring the same path would throw IOException; cancellation proves the held lock was accepted.
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            RunExecutor.Execute(
                new RunConfig { Output = root },
                new Recipe("test", "page", false, _ => Task.CompletedTask),
                grant,
                cancellation.Token,
                held,
                new RunPaths(LockPath, Path.Combine(root, "settings"))
            )
        );
    }

    [Fact]
    public async Task PromotionRejectsACheckpointChangedAfterCapture()
    {
        var png = Path.Combine(root, "checkpoint.png");
        File.WriteAllBytes(png, [1, 2, 3]);
        var docs = Path.Combine(root, "docs-copy");
        var manifest = new RunManifest
        {
            Passed = true,
            DocsRoot = docs,
            Screenshots = new() { ["checkpoint"] = png },
            ScreenshotHashes = new() { ["checkpoint"] = "00" },
        };
        File.WriteAllText(Path.Combine(root, "manifest.json"), JsonSerializer.Serialize(manifest, RunConfig.Json));
        await Assert.ThrowsAsync<IOException>(() => ArtifactFiles.Promote(root, "checkpoint", "out.png", false));
        Assert.False(Directory.Exists(docs));
    }

    [Fact]
    public void PromotionStagesAndVerifiesBytesBeforeReplacingDestination()
    {
        var source = Path.Combine(root, "checkpoint.png");
        var destination = Path.Combine(root, "out.png");
        File.WriteAllBytes(source, [1, 2, 3]);
        var captured = ArtifactFiles.Sha256(source);
        File.WriteAllBytes(destination, [9, 8, 7]);

        // The source changes after the earlier manifest check and before promotion copies it.
        File.WriteAllBytes(source, [4, 5, 6]);
        Assert.Throws<IOException>(() => ArtifactFiles.CopyVerified(source, destination, captured, overwrite: true));
        Assert.Equal(new byte[] { 9, 8, 7 }, File.ReadAllBytes(destination));
        Assert.Equal([source, destination], Directory.GetFiles(root).OrderBy(path => path));
    }

    [Fact]
    public async Task RecoverRequiresExactlyOneBackupDirectory() =>
        Assert.Equal(2, await RunnerCommands.Execute(["recover"]));

    [Fact]
    public void CanceledStepBoundaryRecordsPendingMutationWithoutDispatching()
    {
        var manifest = new RunManifest();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var grant = new Te3Interaction(() => 0);

        Assert.Throws<OperationCanceledException>(() =>
            RunExecutor.ReportStep(manifest, cancellation.Token)("Click Preferences command")
        );
        Assert.Equal("Click Preferences command", manifest.CurrentStep);
    }

    [Fact]
    public void IndexWriteFailurePersistsFinalFailedManifestWithoutReplacingRunFailure()
    {
        var manifestPath = Path.Combine(root, "manifest.json");
        var manifest = new RunManifest
        {
            Passed = true,
            Error = "recipe failed",
            ErrorDetails = "original recipe failure",
        };

        RunExecutor.FinalizeArtifacts(
            manifest,
            () => File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, RunConfig.Json)),
            () => throw new IOException("index unavailable")
        );

        var persisted = JsonSerializer.Deserialize<RunManifest>(File.ReadAllText(manifestPath), RunConfig.Json)!;
        Assert.False(persisted.Passed);
        Assert.Equal("recipe failed", persisted.Error);
        Assert.Equal("original recipe failure", persisted.ErrorDetails);
        Assert.Contains("Writing results: index unavailable", persisted.CleanupError);
    }

    [Fact]
    public async Task PromotionCannotWriteOriginalDocs()
    {
        File.WriteAllText(Path.Combine(root, "test.png"), "preserve");
        var manifest = new RunManifest
        {
            Passed = true,
            DocsRoot = Path.Combine(root, "TabularEditorDocs"),
            Screenshots = new() { ["test"] = Path.Combine(root, "test.png") },
        };
        File.WriteAllText(Path.Combine(root, "manifest.json"), JsonSerializer.Serialize(manifest, RunConfig.Json));
        await Assert.ThrowsAsync<ArgumentException>(() => ArtifactFiles.Promote(root, "test", "test.png", false));
    }

    [Fact]
    public async Task ToolIntentIsJournaledBeforeTheToolRuns()
    {
        // A run killed mid-call must still show which input it was sending.
        var journal = Path.Combine(root, "actions.jsonl");
        string[]? seen = null;
        await RunJournal.Tool(
            journal,
            "windows_click",
            new { @ref = "w1e3" },
            () =>
            {
                seen = File.Exists(journal) ? File.ReadAllLines(journal) : [];
                return Task.FromResult(new FlaUI.Mcp.McpToolResult());
            }
        );
        var intent = JsonDocument.Parse(Assert.Single(seen!)).RootElement;
        Assert.Equal("intent", intent.GetProperty("phase").GetString());
        Assert.Equal("w1e3", intent.GetProperty("arguments").GetProperty("ref").GetString());
        Assert.Equal(2, File.ReadAllLines(journal).Length);
    }

    [Fact]
    public async Task AFailedToolCallIsJournaledAsAnError()
    {
        var journal = Path.Combine(root, "actions.jsonl");
        await Assert.ThrowsAsync<TimeoutException>(() =>
            RunJournal.Tool(
                journal,
                "windows_click",
                new { },
                () => Task.FromException<FlaUI.Mcp.McpToolResult>(new TimeoutException("provider hung"))
            )
        );
        var last = JsonDocument.Parse(File.ReadAllLines(journal)[^1]).RootElement;
        Assert.Equal("error", last.GetProperty("phase").GetString());
        Assert.Contains("provider hung", last.GetProperty("error").GetString());
    }

    [Fact]
    public void LaunchIntentIsPersistedBeforeTe3Starts()
    {
        // A runner killed between Process.Start and the PID save must still read as "a TE3 may be running".
        var manifest = new RunManifest();
        RunManifest? atStart = null;
        RunManifest? lastSaved = null;
        using var app = RunJournal.Launch(
            manifest,
            () => lastSaved = JsonSerializer.Deserialize<RunManifest>(JsonSerializer.Serialize(manifest)),
            new System.Diagnostics.ProcessStartInfo("TabularEditor3.exe"),
            _ =>
            {
                atStart = JsonSerializer.Deserialize<RunManifest>(JsonSerializer.Serialize(manifest));
                // Stands in for the started TE3; nothing is launched.
                return System.Diagnostics.Process.GetCurrentProcess();
            }
        );
        Assert.NotEqual(0, atStart!.LaunchRequestedTicks);
        Assert.Equal(0, atStart.ProcessId);
        Assert.Equal(Environment.ProcessId, manifest.ProcessId);
        // The started process is saved too, so a killed runner can find the TE3 it left.
        Assert.Equal(Environment.ProcessId, lastSaved!.ProcessId);
    }
}
