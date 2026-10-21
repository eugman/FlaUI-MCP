using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using FlaUI.Mcp.Core;

public sealed record RunExecutionResult(string ManifestPath, RunManifest Manifest);

public static class RunExecutor
{
    internal static FileStream AcquireRunnerLock() =>
        new(
            Path.Combine(Path.GetTempPath(), "fla_te3_automation.lock"),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None
        );

    // Engine recipes save into the shared database, so each one starts from the test model again:
    // the same steps as automation/reset-engine.ps1.
    private static async Task ResetEngine(RunConfig config, string database)
    {
        var cli = new CliRunner();
        try
        {
            await cli.Run(
                config.Te,
                ["script", "-s", config.Server, "-d", database, "--file", config.FixtureScript, "--save", "--force"]
            );
            await cli.Run(
                config.Te,
                [
                    "refresh",
                    "-s",
                    config.Server,
                    "-d",
                    database,
                    "--type",
                    "calculate",
                    "--execute",
                    "--force",
                    "--no-progress",
                ]
            );
        }
        catch (InvalidOperationException error)
        {
            throw new InvalidOperationException(
                $"Could not reset {database} on {config.Server} to the test model. If it doesn't exist yet, run "
                    + $"pwsh automation/reset-engine.ps1 -Create -Server {config.Server} -DatabaseName {database}. {error.Message}",
                error
            );
        }
    }

    public static async Task<string> CreateModel(RunConfig config, string directory, string name)
    {
        var path = Path.Combine(directory, name + ".bim");
        if (config.OfflineBaseline != null)
        {
            File.Copy(config.OfflineBaseline, path, false);
        }
        else
        {
            var script = Path.Combine(directory, "fixture.csx");
            File.Copy(config.FixtureScript, script, false);
            var cli = new CliRunner();
            await cli.Run(
                config.Te,
                ["init", path, "--serialization", "bim", "--name", name, "--compatibility-mode", "AnalysisServices"]
            );
            await cli.Run(config.Te, ["script", "--model", path, "--file", script, "--save"]);
        }

        return path;
    }

    public static Task<RunExecutionResult> Execute(
        RunConfig config,
        Recipe recipe,
        CancellationToken cancellationToken = default
    ) => Execute(config, recipe, cancellationToken, heldRunnerLock: null);

    // A batch passes its held lock so no other runner can start between recipes.
    internal static async Task<RunExecutionResult> Execute(
        RunConfig config,
        Recipe recipe,
        CancellationToken cancellationToken,
        FileStream? heldRunnerLock
    )
    {
        using var runnerGate = heldRunnerLock == null ? AcquireRunnerLock() : null;
        cancellationToken.ThrowIfCancellationRequested();
        if (recipe.RequiresServer && !config.UseServer(recipe))
        {
            throw new ArgumentException("Recipe requires fixed/auto fixture mode: " + recipe.Id);
        }

        RunSafety.RequireExclusiveTe3();
        var id = "fla_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N")[..8];
        var output = Path.Combine(config.Output, id);
        Directory.CreateDirectory(output);
        var manifest = new RunManifest
        {
            RunId = id,
            RequestedScenario = recipe.Id,
            Output = output,
            Te = config.Te,
            Te3Path = config.Te3,
            Server = config.Server,
            Database = config.UseServer(recipe) ? config.FixedSlot : "",
            DocsRoot = config.DocsRoot,
            CaptureVariant = config.CaptureVariant,
            ExpectedDpi = config.ExpectedDpi,
        };
        var manifestPath = Path.Combine(output, "manifest.json");
        var journal = Path.Combine(output, "actions.jsonl");
        var inputs = new InputTally();
        void Save() => AtomicJournal.Write(manifestPath, manifest, RunConfig.Json);
        Save();
        SettingsLease? settings = null;
        AutomationHost? host = null;
        Process? app = null;
        string? handle = null;
        Dictionary<string, string>? settingsBefore = null;
        var timer = Stopwatch.StartNew();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var model = manifest.Database.Length == 0 ? await CreateModel(config, output, id) : "";
            if (manifest.Database.Length == 0)
            {
                manifest.SourceHashes["fixture.bim"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(model)));
                OfflineModelOptions.Create(model, output, Environment.UserName);
            }
            else
            {
                await ResetEngine(config, manifest.Database);
            }

            cancellationToken.ThrowIfCancellationRequested();
            RunSafety.RequireExclusiveTe3();
            settingsBefore = SettingsChanges.Snapshot(SettingsLease.DefaultDirectory);
            settings = AcquireSettings(manifest, Save);
            settings.Normalize(config.MaximizeWindow);
            cancellationToken.ThrowIfCancellationRequested();
            host = new AutomationHost(new ProcessPolicy(["TabularEditor3"]));
            var start = new ProcessStartInfo(config.Te3) { UseShellExecute = false, CreateNoWindow = true };
            if (manifest.Database.Length > 0)
            {
                start.ArgumentList.Add(config.Server);
                start.ArgumentList.Add(manifest.Database);
            }
            else
            {
                start.ArgumentList.Add(model);
            }

            app = RunJournal.Launch(manifest, Save, start, Process.Start);
            manifest.Te3Version = app.MainModule?.FileVersionInfo.ProductVersion;
            var marker = manifest.Database.Length > 0 ? manifest.Database : id;
            handle = await WaitForOwnedWindow(host, app, marker, cancellationToken);
            await ActivateOwnedWindow(host, handle, app.Id, $"{manifest.Database} {id}", journal, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var page = new Te3Page(host, handle, Invoke, ReportStep(manifest, cancellationToken), journal, inputs);
            await page.DefaultLayout();
            cancellationToken.ThrowIfCancellationRequested();
            await recipe.Execute(new RecipeContext(page, config, manifest, model, Save));
            cancellationToken.ThrowIfCancellationRequested();
            manifest.Tests.Add(new(recipe.Id, "passed", timer.ElapsedMilliseconds, recipe.DocsPage));
            manifest.Passed = true;
        }
        catch (Exception ex)
        {
            RecordFailure(manifest, ex);
            manifest.Tests.Add(new(recipe.Id, "failed", timer.ElapsedMilliseconds, recipe.DocsPage));
            if (handle != null)
            {
                try
                {
                    await Invoke(
                        "windows_screenshot",
                        new
                        {
                            handle,
                            savePath = Path.Combine(output, "failure.png"),
                            includeImage = false,
                            background = true,
                        }
                    );
                }
                catch { }

                try
                {
                    await Invoke("windows_find", new { handle, maxResults = 200 });
                }
                catch { }
            }

            try
            {
                WriteWindowList(Path.Combine(output, "failure-windows.json"), manifest.ProcessId);
            }
            catch { }
        }
        finally
        {
            var cleanupErrors = new List<string>();
            var closed = false;
            try
            {
                await RunSafety.CloseOwned(manifest.ProcessId, manifest.ProcessStartedTicks, manifest.Te3Path, journal);
                RunSafety.RequireExclusiveTe3();
                closed = true;
            }
            catch (Exception error)
            {
                cleanupErrors.Add("TE3 shutdown: " + error.Message);
            }

            if (closed && settings != null)
            {
                try
                {
                    settings.Restore();
                    manifest.SettingsRestored = true;
                }
                catch (Exception error)
                {
                    cleanupErrors.Add("Preferences: " + error.Message);
                }
            }
            // Compared whether or not TE3 closed cleanly. A run that had to be killed is when
            // settings are *most* likely to have been left changed, and skipping the comparison
            // there recorded nothing about the case that matters most.
            if (settingsBefore != null)
            {
                try
                {
                    manifest.SettingsChanges = SettingsChanges.Compare(
                        settingsBefore,
                        SettingsChanges.Snapshot(SettingsLease.DefaultDirectory)
                    );
                }
                catch (Exception error)
                {
                    manifest.SettingsChanges = ["snapshot failed: " + error.Message];
                }
            }

            try
            {
                app?.Dispose();
                host?.Dispose();
            }
            catch (Exception error)
            {
                cleanupErrors.Add("Disposal: " + error.Message);
            }
            // Only a leased file left changed is a recovery condition: the restore did not take
            // and a backup exists to put it back. Every other file in the folder is churn TE3
            // writes on a normal launch, which nothing restores — making those block the run left
            // it permanently "needs recovery" with no action that could clear it, and
            // ArtifactFiles.Promote refuses every capture from such a run.
            manifest.NeedsRecovery =
                !closed
                || !manifest.SettingsRestored
                || cleanupErrors.Count > 0
                || SettingsChanges.Unrestored(manifest.SettingsChanges).Count > 0;
            if (manifest.NeedsRecovery)
            {
                manifest.Passed = false;
                manifest.CleanupError =
                    string.Join("; ", cleanupErrors) + $". Retained settings backup: {manifest.SettingsBackup}";
            }

            FinalizeArtifacts(manifest, Save, () => ArtifactFiles.WriteIndex(manifest));
        }

        return new(manifestPath, manifest);

        async Task Invoke(string tool, object arguments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RunSafety.RequireExclusiveTe3(manifest.ProcessId);
            await inputs.Invoke(
                () =>
                    RunJournal.Tool(
                        journal,
                        tool,
                        arguments,
                        () => host!.Tools.ExecuteToolAsync(tool, JsonSerializer.SerializeToElement(arguments))
                    ),
                tool is "windows_screenshot" or "windows_find",
                tool
            );
        }
    }

    // The run's TE3 window: the only top-level window titled with the run's model or database.
    private static async Task<string> WaitForOwnedWindow(
        AutomationHost host,
        Process app,
        string marker,
        CancellationToken cancellationToken
    )
    {
        var startup = Stopwatch.StartNew();
        while (startup.Elapsed < TimeSpan.FromSeconds(90))
        {
            var windows = Win32Desktop
                .GetTopLevelWindows(app.Id)
                .Where(w => !w.IsToolWindow && w.Title.Contains(marker) && w.Title.Contains("Tabular Editor 3"))
                .ToArray();
            if (windows.Length == 1)
            {
                return host.Sessions.RegisterNativeWindow(windows[0].Hwnd, app.Id);
            }

            if (app.HasExited)
            {
                throw new InvalidOperationException("TE3 exited during startup");
            }

            await Task.Delay(250, cancellationToken);
        }

        throw new TimeoutException("TE3 did not open the owned model");
    }

    internal const string ActivationTimeout =
        "Manual activation not received within 120 seconds. The run asked Windows to focus TE3; nothing was typed or clicked.";

    // Asks Windows to focus the run's TE3, then waits for the user to click it if Windows refused.
    private static async Task ActivateOwnedWindow(
        AutomationHost host,
        string handle,
        int processId,
        string label,
        string journal,
        CancellationToken cancellationToken
    )
    {
        await RunJournal.Input(journal, "te3_focus_window", new { handle }, () => host.Sessions.FocusWindow(handle));
        var target = host.Sessions.GetInputTarget(handle);
        var activation = Stopwatch.StartNew();
        if (Win32Desktop.GetForegroundWindow() != target.Hwnd)
        {
            Console.WriteLine($"Click the TE3 window containing {label}; waiting 120 seconds.");
        }

        while (Win32Desktop.GetForegroundWindow() != target.Hwnd)
        {
            RunSafety.RequireExclusiveTe3(processId);
            target.EnsureAlive();
            if (activation.Elapsed > TimeSpan.FromSeconds(120))
            {
                throw new TimeoutException(ActivationTimeout);
            }

            await Task.Delay(250, cancellationToken);
        }
    }

    internal static void RecordFailure(RunManifest manifest, Exception error)
    {
        manifest.Error = string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message;
        manifest.ErrorDetails = error.ToString();
        manifest.JournalError = RunJournal.ErrorNote(error);
    }

    // The failure capture shows only the main window; this z-ordered list shows what overlapped it.
    // Titles of other applications' windows are omitted.
    internal static void WriteWindowList(string path, int te3ProcessId) =>
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(
                Win32Desktop
                    .GetTopLevelWindows()
                    .Select(w => new
                    {
                        hwnd = w.Hwnd.ToInt64(),
                        w.ProcessId,
                        title = w.ProcessId == te3ProcessId ? w.Title : null,
                        w.IsCloaked,
                        bounds = Win32Desktop.GetWindowBounds(w.Hwnd),
                    }),
                RunConfig.Json
            )
        );

    internal static Action<string> ReportStep(RunManifest manifest, CancellationToken cancellationToken) =>
        step =>
        {
            manifest.CurrentStep = step;
            cancellationToken.ThrowIfCancellationRequested();
        };

    internal static SettingsLease AcquireSettings(RunManifest manifest, Action save, Func<SettingsLease>? create = null)
    {
        var settings = (
            create ?? (() => new SettingsLease(manifestPath: Path.Combine(manifest.Output, "manifest.json")))
        )();
        // Persist before normalization so a hard-killed run still reads as needing recovery.
        manifest.SettingsBackup = settings.BackupDirectory;
        manifest.SettingsRestored = false;
        manifest.NeedsRecovery = true;
        save();
        return settings;
    }

    internal static void FinalizeArtifacts(RunManifest manifest, Action save, Action writeIndex)
    {
        try
        {
            save();
            writeIndex();
        }
        catch (Exception error)
        {
            manifest.Passed = false;
            var reportingError = "Writing results: " + error.Message;
            manifest.CleanupError = string.IsNullOrWhiteSpace(manifest.CleanupError)
                ? reportingError
                : manifest.CleanupError + "; " + reportingError;
            try
            {
                save();
            }
            catch (Exception saveError)
            {
                manifest.CleanupError += "; Final manifest save: " + saveError.Message;
            }

            Console.Error.WriteLine(manifest.CleanupError);
        }
    }
}
