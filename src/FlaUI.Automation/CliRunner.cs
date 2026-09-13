using System.Diagnostics;
using System.Text.Json;
using PlaywrightWindows.Mcp.Core;

public interface ICliRunner
{
    Task<string> Run(string executable, IEnumerable<string> arguments);
}

public sealed class CliRunner : ICliRunner
{
    private static string PendingPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FlaUI-MCP", "pending-cli.json");
    private sealed record PendingCli(int ProcessId, long StartedTicks, string Executable);
    private static PendingCli? active;
    public static void RequireIdle()
    {
        if (active != null) RequireExited(active);
        active = null;
        if (!File.Exists(PendingPath)) return;
        RequireExited(JsonSerializer.Deserialize<PendingCli>(File.ReadAllText(PendingPath)) ?? throw new IOException("Invalid pending CLI record"));
        File.Delete(PendingPath);
    }
    private static void RequireExited(PendingCli previous)
    {
        Process? process = null;
        try { process = Process.GetProcessById(previous.ProcessId); } catch (ArgumentException) { }
        using (process)
        {
            if (process != null && !process.HasExited && (previous.StartedTicks == 0 || process.StartTime.ToUniversalTime().Ticks == previous.StartedTicks))
                throw new InvalidOperationException($"Previous TE CLI PID {previous.ProcessId} has not exited; no new server operation started");
        }
    }
    public async Task<string> Run(string executable, IEnumerable<string> arguments)
    {
        RequireIdle();
        var start = new ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.ArgumentList.Add("--non-interactive"); start.ArgumentList.Add("--output-format"); start.ArgumentList.Add("json");
        start.Environment["TE_SESSION"] = "fla_automation_" + Environment.ProcessId;
        var fullExecutable = Path.GetFullPath(executable);
        Directory.CreateDirectory(Path.GetDirectoryName(PendingPath)!);
        using var process = Process.Start(start)!;
        active = new PendingCli(process.Id, 0, fullExecutable);
        try
        {
            active = active with { StartedTicks = process.StartTime.ToUniversalTime().Ticks };
            AtomicJournal.Write(PendingPath, active, RunConfig.Json);
        }
        catch
        {
            await ConfirmStopped(() => process.Kill(true), token => process.WaitForExitAsync(token));
            active = null;
            throw;
        }
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            await ConfirmStopped(() => { if (!process.HasExited) process.Kill(true); }, token => process.WaitForExitAsync(token));
            active = null;
            File.Delete(PendingPath);
            throw new TimeoutException("TE CLI exceeded four-minute timeout; child exit confirmed before recovery.");
        }
        active = null;
        File.Delete(PendingPath); // Only after WaitForExitAsync confirms termination.
        var output = await stdout; var error = await stderr;
        if (process.ExitCode != 0) throw new InvalidOperationException($"TE CLI failed ({process.ExitCode}): {output}\n{error}");
        return output;
    }
    internal static async Task ConfirmStopped(Action kill, Func<CancellationToken, Task> waitForExit)
    {
        try
        {
            kill();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await waitForExit(deadline.Token);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("TE CLI shutdown is unconfirmed; pending-cli.json is retained and further CLI/reset operations are blocked", ex);
        }
    }
}


public static class CliProtocol
{
    // Observed with installed TE CLI: success + messages[{level:"output",text:...}].
    // Never accept an echoed sentinel from diagnostics or arbitrary nested metadata.
    public static string[] Output(JsonElement root)
    {
        if (!root.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True ||
            !root.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Unrecognized or unsuccessful TE CLI response");
        return messages.EnumerateArray().Where(m => m.TryGetProperty("level", out var level) && level.GetString() == "output")
            .Select(m => m.GetProperty("text").GetString() ?? "").ToArray();
    }
    public static void Require(string json, string expected)
    {
        using var doc = JsonDocument.Parse(json);
        var messages = Output(doc.RootElement);
        if (messages.Length != 1 || messages[0] != expected) throw new InvalidOperationException("Unexpected TE CLI output protocol");
    }
}

public static class AtomicJournal
{
    public static void Write<T>(string path, T value, JsonSerializerOptions options)
    {
        var target = Path.GetFullPath(path);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, value, options); stream.Flush(true); }
            File.Move(temporary, target, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
