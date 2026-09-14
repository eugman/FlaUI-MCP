using System.Text.Json;
using PlaywrightWindows.Mcp.Core;

return await RunnerCommands.Execute(args);

public static class RunnerCommands
{
    public const string Usage = """
        FlaUI.Automation run CONFIG [--scenario ID] [--repeat N]
        FlaUI.Automation list|validate CONFIG
        FlaUI.Automation compose MANIFEST SPEC OUT.png
        FlaUI.Automation promote RUN_DIR CHECKPOINT DEST.png [--overwrite]
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
            if (command == "promote")
            {
                if (args.Length is not (4 or 5) || args.Length == 5 && args[4] != "--overwrite") throw new ArgumentException(Usage);
                await ArtifactFiles.Promote(args[1], args[2], args[3], args.Length == 5); return 0;
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
            string? selector = null; int? repeat = null;
            for (var i = 2; i < args.Length; i += 2)
            {
                if (command != "run" || i + 1 >= args.Length) throw new ArgumentException(Usage);
                if (args[i] == "--scenario" && selector == null) selector = args[i + 1];
                else if (args[i] == "--repeat" && repeat == null && int.TryParse(args[i + 1], out var value) && value is >= 1 and <= 100) repeat = value;
                else throw new ArgumentException(Usage);
            }
            if (command == "list")
            {
                foreach (var recipe in RecipeCatalog.All) Console.WriteLine($"{recipe.Id}  {(recipe.RequiresServer ? "engine" : "offline")}  {recipe.DocsPage}");
                return 0;
            }
            config.ValidateFiles();
            var recipes = RecipeCatalog.Select(selector == null ? config.Scenarios : [selector]);
            if (recipes.Any(r => r.RequiresServer && !config.UseServer(r))) throw new ArgumentException("Selected recipes require fixed/auto fixture mode");
            if (command == "validate") { Console.WriteLine("Valid: " + string.Join(", ", recipes.Select(r => r.Id))); return 0; }
            using var interrupts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; interrupts.Cancel(); };
            Console.CancelKeyPress += cancel;
            try
            {
                using var runnerGate = RunExecutor.AcquireRunnerLock();
                DpiUtility.EnablePerMonitorV2();
                foreach (var recipe in recipes)
                    for (var n = 0; n < (repeat ?? config.Repeat); n++)
                    {
                        var result = await RunExecutor.Execute(config, recipe, interrupts.Token, runnerGate);
                        Console.WriteLine(JsonSerializer.Serialize(new { result.ManifestPath, result.Manifest.Passed, result.Manifest.Error, result.Manifest.CleanupError }));
                        if (!result.Manifest.Passed) return 1;
                    }
                return 0;
            }
            finally { Console.CancelKeyPress -= cancel; }
        }
        catch (OperationCanceledException) { Console.Error.WriteLine("Operation canceled; cleanup was attempted if a run started."); return 1; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or JsonException or TimeoutException or AggregateException)
        { Console.Error.WriteLine(ex.GetType().Name + ": " + ex.Message); return 2; }
    }
}
