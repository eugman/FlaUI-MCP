using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using PlaywrightWindows.Mcp.Core;

public sealed record RunExecutionResult(string ManifestPath, RunManifest Manifest);

public static class RunExecutor
{
    public static string SlotRegistry => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FlaUI-MCP", "fixture-slots.json");
    public static FixtureSlots Slots(RunConfig config) => new(SlotRegistry, new CalculatedFixtureBackend(config.Te, new CliRunner()));

    public static async Task<string> CreateModel(RunConfig config, string directory, string name)
    {
        var path = Path.Combine(directory, name + ".bim");
        if (config.OfflineBaseline != null) File.Copy(config.OfflineBaseline, path, false);
        else
        {
            var script = Path.Combine(directory, "fixture.csx");
            File.Copy(config.FixtureScript, script, false);
            var cli = new CliRunner();
            await cli.Run(config.Te, ["init", path, "--serialization", "bim", "--name", name, "--compatibility-mode", "AnalysisServices"]);
            await cli.Run(config.Te, ["script", path, "--script", script, "--save"]);
        }
        _ = ModelStateVerification.Hash(File.ReadAllText(path));
        return path;
    }
    public static string RecipeHash(RunConfig config) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(config.OfflineBaseline ?? config.FixtureScript)));
    public static SlotRegistration Registration(RunConfig config, string model, SlotRegistration? existing)
    {
        var retained = FixtureBaseline.Retain(model, Path.Combine(Path.GetDirectoryName(SlotRegistry)!, "baselines"));
        return new(config.Server, config.FixedSlot, existing?.Owner ?? Guid.NewGuid().ToString("N"), retained,
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(retained))), RecipeHash: RecipeHash(config));
    }
    public static async Task<RunExecutionResult> Execute(RunConfig config, Recipe recipe)
    {
        if (recipe.RequiresServer && !config.UseServer(recipe)) throw new ArgumentException("Recipe requires fixed/auto fixture mode: " + recipe.Id);
        RunSafety.RequireExclusiveTe3();
        var id = "fla_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N")[..8];
        var output = Path.Combine(config.Output, id); Directory.CreateDirectory(output);
        var manifest = new RunManifest { RunId = id, RequestedScenario = recipe.Id, Output = output, Te = config.Te,
            Te3Path = config.Te3, Server = config.Server, Database = config.UseServer(recipe) ? config.FixedSlot : "", DocsRoot = config.DocsRoot,
            CaptureVariant = config.CaptureVariant, ExpectedDpi = config.ExpectedDpi };
        var manifestPath = Path.Combine(output, "manifest.json");
        void Save() => AtomicJournal.Write(manifestPath, manifest, RunConfig.Json);
        Save();
        SettingsLease? settings = null; AutomationHost? host = null; Process? app = null; string? handle = null;
        var timer = Stopwatch.StartNew();
        try
        {
            var slots = Slots(config);
            var existing = manifest.Database.Length > 0 ? slots.Status().SingleOrDefault(s => s.Database == config.FixedSlot) : null;
            string model;
            if (existing != null)
            {
                if (existing.Server != config.Server || existing.RecipeHash != RecipeHash(config))
                    throw new InvalidOperationException("Fixture source/server changed; use fixture-reset explicitly");
                FixtureSlots.Validate(existing);
                model = Path.Combine(output, id + ".bim"); File.Copy(existing.BaselinePath, model, false);
            }
            else model = await CreateModel(config, output, id);
            manifest.SourceHashes["fixture.bim"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(model)));
            if (manifest.Database.Length > 0)
            {
                manifest.Slot = (existing ?? Registration(config, model, null)) with { Dirty = true, PendingOperation = null };
                manifest.SlotRegistry = SlotRegistry; Save();
                await slots.Reset(manifest.Slot); slots.MarkDirty(config.FixedSlot);
            }
            else OfflineModelOptions.Create(model, output, Environment.UserName);
            RunSafety.RequireExclusiveTe3();
            settings = AcquireSettings(manifest, Save); settings.Normalize(config.MaximizeWindow);
            host = new AutomationHost(new ProcessPolicy(["TabularEditor3"]));
            var start = new ProcessStartInfo(config.Te3) { UseShellExecute = false, CreateNoWindow = true };
            if (manifest.Database.Length > 0) { start.ArgumentList.Add(config.Server); start.ArgumentList.Add(manifest.Database); }
            else start.ArgumentList.Add(model);
            app = Process.Start(start) ?? throw new InvalidOperationException("TE3 did not start");
            manifest.ProcessId = app.Id; manifest.ProcessStartedTicks = app.StartTime.ToUniversalTime().Ticks; Save();
            manifest.Te3Version = app.MainModule?.FileVersionInfo.ProductVersion;
            var startup = Stopwatch.StartNew();
            while (startup.Elapsed < TimeSpan.FromSeconds(90))
            {
                var marker = manifest.Database.Length > 0 ? manifest.Database : id;
                var windows = Win32Desktop.GetTopLevelWindows(app.Id).Where(w => !w.IsToolWindow && w.Title.Contains(marker) && w.Title.Contains("Tabular Editor 3")).ToArray();
                if (windows.Length == 1) { handle = host.Sessions.RegisterNativeWindow(windows[0].Hwnd, app.Id); break; }
                if (app.HasExited) throw new InvalidOperationException("TE3 exited during startup");
                await Task.Delay(250);
            }
            if (handle == null) throw new TimeoutException("TE3 did not open the owned model");
            host.Sessions.FocusWindow(handle);
            var target = host.Sessions.GetInputTarget(handle);
            var activation = Stopwatch.StartNew();
            if (Win32Desktop.GetForegroundWindow() != target.Hwnd)
                Console.WriteLine($"Click the TE3 window containing {manifest.Database} {id}; waiting 120 seconds.");
            while (Win32Desktop.GetForegroundWindow() != target.Hwnd)
            {
                RunSafety.RequireExclusiveTe3(app.Id); target.EnsureAlive();
                if (activation.Elapsed > TimeSpan.FromSeconds(120)) throw new TimeoutException("Manual activation not received; no input sent");
                await Task.Delay(250);
            }
            var page = new Te3Page(host, handle, Invoke, step => manifest.CurrentStep = step);
            await page.DefaultLayout();
            await recipe.Execute(new RecipeContext(page, config, manifest, model, Save));
            manifest.Tests.Add(new(recipe.Id, "passed", timer.ElapsedMilliseconds, recipe.DocsPage));
            manifest.Passed = true;
        }
        catch (Exception ex)
        {
            RecordFailure(manifest, ex);
            manifest.Tests.Add(new(recipe.Id, "failed", timer.ElapsedMilliseconds, recipe.DocsPage));
            if (handle != null)
            {
                try { await Invoke("windows_screenshot", new { handle, savePath = Path.Combine(output, "failure.png"), includeImage = false, background = true }); } catch { }
                try { await Invoke("windows_find", new { handle, maxResults = 200 }); } catch { }
            }
        }
        finally
        {
            try { await RecoveryCoordinator.Recover(manifest, true, Save, settings); }
            finally { app?.Dispose(); host?.Dispose(); }
            ArtifactFiles.WriteIndex(manifest);
        }
        return new(manifestPath, manifest);

        async Task Invoke(string tool, object arguments)
        {
            RunSafety.RequireExclusiveTe3(manifest.ProcessId);
            var result = await host!.Tools.ExecuteToolAsync(tool, JsonSerializer.SerializeToElement(arguments));
            File.AppendAllText(Path.Combine(output, "actions.jsonl"), JsonSerializer.Serialize(new { tool, arguments, result }) + Environment.NewLine);
            if (result.IsError == true || result.Outcome?.IsPending == true)
                throw new InvalidOperationException(string.Join("; ", result.Content.Select(c => c.Text)));
        }
    }
    internal static void RecordFailure(RunManifest manifest, Exception error)
    {
        manifest.Error = string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message;
        manifest.ErrorDetails = error.ToString();
    }
    internal static SettingsLease AcquireSettings(RunManifest manifest, Action save, Func<SettingsLease>? create = null)
    {
        var settings = (create ?? (() => new SettingsLease()))();
        manifest.SettingsBackup = settings.BackupDirectory; manifest.SettingsRestored = false; save();
        return settings;
    }
}
