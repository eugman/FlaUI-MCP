using System.Text.Json;
using PlaywrightWindows.Mcp.Core;

return await RunnerCommands.Execute(args);

public static class RunnerCommands
{
    public const string Usage = """
        FlaUI.Automation run CONFIG [--scenario ID[,ID...]] [--repeat N] [--continue]
        FlaUI.Automation list|validate CONFIG
        FlaUI.Automation compose MANIFEST SPEC OUT.png
        FlaUI.Automation annotate MANIFEST ANNOTATION_SPEC
        FlaUI.Automation promote RUN_DIR CHECKPOINT DEST.png [--overwrite]
        FlaUI.Automation promote RUN_DIR --item ITEM_ID [--overwrite]
        FlaUI.Automation mcp
        FlaUI.Automation recover BACKUP_DIR
        --no-focus rejects run.
        """;
    public static Task<int> Execute(string[] args) => Execute(args, CancellationToken.None);

    internal static async Task<int> Execute(string[] args, CancellationToken cancellationToken)
    {
        try
        {
            if (args.Length >= 1 && args[0] == "mcp")
            {
                if (args.Length != 1) throw new ArgumentException(Usage);
                DpiUtility.EnablePerMonitorV2();
                using var host = new AutomationHost(new ProcessPolicy(["TabularEditor3"]));
                var registry = new PlaywrightWindows.Mcp.ToolRegistry();
                foreach (var name in Te3Companion.Names) registry.RegisterTool(new Te3Companion(host, name));
                await new PlaywrightWindows.Mcp.McpServer(registry).RunAsync();
                return 0;
            }
            var noFocus = args.Contains("--no-focus"); args = args.Where(a => a != "--no-focus").ToArray();
            if (args.Length < 2) throw new ArgumentException(Usage);
            var command = args[0];
            if (noFocus && command is ("run" or "recover"))
                throw new ArgumentException("Mutating/desktop command rejected by --no-focus");
            if (command == "compose")
            {
                if (args.Length != 4) throw new ArgumentException(Usage);
                CaptureComposition.Create(args[1], args[2], args[3]); return 0;
            }
            if (command == "annotate")
            {
                if (args.Length != 3) throw new ArgumentException(Usage);
                CaptureAnnotations.Create(args[1], args[2]); return 0;
            }
            if (command == "promote")
            {
                if (args.Length is not (4 or 5) || args.Length == 5 && args[4] != "--overwrite") throw new ArgumentException(Usage);
                if (args[2] == "--item") await ArtifactFiles.PromoteItem(args[1], args[3], args.Length == 5);
                else await ArtifactFiles.Promote(args[1], args[2], args[3], args.Length == 5);
                return 0;
            }
            if (command == "recover")
            {
                if (args.Length != 2) throw new ArgumentException(Usage);
                // Restores an interrupted run's exact backup; hashes are checked before and after copying.
                var backup = Path.GetFullPath(args[1]);
                using var recoveryGate = RunExecutor.AcquireRunnerLock();
                RunSafety.RequireExclusiveTe3();
                SettingsLease.RestoreBackup(backup);
                Console.WriteLine(JsonSerializer.Serialize(new { restored = backup, verified = "sha256", backupRetained = true }));
                return 0;
            }
            if (command is not ("run" or "list" or "validate"))
                throw new ArgumentException(Usage);
            var config = RunConfig.Read(args[1]);
            var options = RunOptions.Parse(command, args[2..]);
            if (command == "list")
            {
                foreach (var recipe in RecipeCatalog.All) Console.WriteLine($"{recipe.Id}  {(recipe.RequiresServer ? "engine" : "offline")}  {recipe.DocsPage}");
                return 0;
            }
            config.ValidateFiles();
            var recipes = RecipeCatalog.Select(options.Scenarios ?? config.Scenarios);
            if (recipes.Any(r => r.RequiresServer && !config.UseServer(r))) throw new ArgumentException("Selected recipes require fixed/auto fixture mode");
            if (command == "validate") { Console.WriteLine("Valid: " + string.Join(", ", recipes.Select(r => r.Id))); return 0; }
            using var interrupts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; interrupts.Cancel(); };
            Console.CancelKeyPress += cancel;
            try
            {
                using var runnerGate = RunExecutor.AcquireRunnerLock();
                DpiUtility.EnablePerMonitorV2();
                var failed = false;
                foreach (var recipe in recipes)
                    for (var n = 0; n < (options.Repeat ?? config.Repeat); n++)
                    {
                        var result = await RunExecutor.Execute(config, recipe, interrupts.Token, runnerGate);
                        Console.WriteLine(JsonSerializer.Serialize(new { recipe = recipe.Id, result.ManifestPath, result.Manifest.Passed, result.Manifest.Error, result.Manifest.CleanupError }));
                        if (result.Manifest.Passed) continue;
                        failed = true;
                        // A failed recipe can be skipped; a desktop that was not restored cannot.
                        if (!options.Continue || !RunOptions.SafeToContinue(result.Manifest)) return 1;
                    }
                return failed ? 1 : 0;
            }
            finally { Console.CancelKeyPress -= cancel; }
        }
        catch (OperationCanceledException) { Console.Error.WriteLine("Operation canceled; cleanup was attempted if a run started."); return 1; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or JsonException or TimeoutException or AggregateException)
        { Console.Error.WriteLine(ex.GetType().Name + ": " + ex.Message); return 2; }
    }
}

public sealed record RunOptions(string[]? Scenarios, int? Repeat, bool Continue)
{
    public static RunOptions Parse(string command, string[] args)
    {
        string[]? scenarios = null; int? repeat = null; var keepGoing = false;
        for (var i = 0; i < args.Length; i++)
        {
            if (command != "run") throw new ArgumentException(RunnerCommands.Usage);
            if (args[i] == "--continue" && !keepGoing) { keepGoing = true; continue; }
            if (i + 1 >= args.Length) throw new ArgumentException(RunnerCommands.Usage);
            if (args[i] == "--scenario" && scenarios == null)
                scenarios = args[++i].Split(',', StringSplitOptions.TrimEntries);
            else if (args[i] == "--repeat" && repeat == null && int.TryParse(args[i + 1], out var value) && value is >= 1 and <= 100)
            { repeat = value; i++; }
            else throw new ArgumentException(RunnerCommands.Usage);
        }
        return new(scenarios, repeat, keepGoing);
    }

    public static bool SafeToContinue(RunManifest manifest) =>
        !manifest.NeedsRecovery && manifest.SettingsRestored && string.IsNullOrEmpty(manifest.CleanupError);
}
