using System.Diagnostics;
using System.Text.Json;
using PlaywrightWindows.Mcp.Core;

// Reuse the screenshot runner for exclusive ownership, startup and restoration.
if (args.Length is not (3 or 4) || args[0] != "hold") throw new ArgumentException("hold CONFIG OUTPUT [MINUTES]");
var holdMinutes = args.Length == 4 ? int.Parse(args[3]) : 10;
var output = Path.GetFullPath(args[2]);
Directory.CreateDirectory(output);
if (File.Exists(Path.Combine(output, "ready.json")) || File.Exists(Path.Combine(output, "done")))
    throw new IOException("Use a fresh trial directory");
var config = RunConfig.Read(args[1]) with { Output = output };
config.ValidateFiles();
// Tasks that need an engine (dax-query) hold an existing fla_ database instead of the offline fixture.
if (config.FixtureMode is not ("offline" or "fixed")) throw new ArgumentException("Study requires an offline fixture or a fixed fla_ database");
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
DpiUtility.EnablePerMonitorV2();
var result = await RunExecutor.Execute(config, new Recipe("study-hold", "", false, async context =>
{
    using var process = Process.GetProcessesByName("TabularEditor3").Single();
    var environment = context.Page.ObserveCaptureEnvironment();
    environment.RequireScale(config.ExpectedDpi);
    var ready = Path.Combine(output, "ready.json");
    File.WriteAllText(ready + ".tmp", JsonSerializer.Serialize(new { processId = process.Id, environment }));
    File.Move(ready + ".tmp", ready);
    var timer = Stopwatch.StartNew();
    while (!File.Exists(Path.Combine(output, "done")))
    {
        if (timer.Elapsed > TimeSpan.FromMinutes(holdMinutes)) throw new TimeoutException("Agent handoff expired");
        if (process.HasExited) throw new InvalidOperationException("Test TE3 exited");
        await Task.Delay(250, cancellation.Token);
    }
}), cancellation.Token);
Console.WriteLine(JsonSerializer.Serialize(result));
// Exit status reports restoration only; the manifest records whether the hold itself passed.
return result.Manifest.NeedsRecovery || !result.Manifest.SettingsRestored || !string.IsNullOrEmpty(result.Manifest.CleanupError) ? 1 : 0;
