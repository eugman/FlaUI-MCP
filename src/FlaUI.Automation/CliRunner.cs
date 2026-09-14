using System.Diagnostics;
using System.Text.Json;

public sealed class CliRunner
{
    public async Task<string> Run(string executable, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.ArgumentList.Add("--non-interactive");
        start.ArgumentList.Add("--output-format"); start.ArgumentList.Add("json");
        start.Environment["TE_SESSION"] = "fla_automation_" + Environment.ProcessId;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("TE CLI did not start");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            var output = await stdout;
            var error = await stderr;
            if (process.ExitCode != 0) throw new InvalidOperationException($"TE CLI failed ({process.ExitCode}): {output}\n{error}");
            return output;
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await process.WaitForExitAsync(shutdown.Token);
            }
            catch (Exception error)
            {
                throw new TimeoutException($"TE CLI timed out; shutdown of PID {process.Id} could not be confirmed. Inspect it before another run.", error);
            }
            throw new TimeoutException("TE CLI exceeded four minutes and was terminated. Inspect state before retrying.");
        }
    }
}

public static class AtomicJournal
{
    public static void Write<T>(string path, T value, JsonSerializerOptions options)
        => Write(path, value, options, File.Delete);

    internal static void Write<T>(string path, T value, JsonSerializerOptions options, Action<string> deleteTemporary)
    {
        var target = Path.GetFullPath(path);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        Exception? writeError = null;
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, value, options); stream.Flush(true); }
            File.Move(temporary, target, true);
        }
        catch (Exception error)
        {
            writeError = error;
            throw;
        }
        finally
        {
            try { if (File.Exists(temporary)) deleteTemporary(temporary); }
            catch (Exception cleanupError) when (writeError != null)
            {
                throw new AggregateException($"Journal write failed and temporary file cleanup also failed: {temporary}", writeError, cleanupError);
            }
        }
    }
}
