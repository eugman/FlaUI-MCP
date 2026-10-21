using System.Diagnostics;
using System.Text.Json;
using FlaUI.Mcp;

/// <summary>What a run records before it acts, so a runner killed mid-step still leaves evidence of it.</summary>
internal static class RunJournal
{
    /// <summary>Exception.Data key for a journal entry that could not be written after the call.</summary>
    internal const string JournalErrorKey = "journalError";

    /// <summary>Persists the launch intent, starts TE3, then records the owned PID and start time.</summary>
    internal static Process Launch(
        RunManifest manifest,
        Action save,
        ProcessStartInfo start,
        Func<ProcessStartInfo, Process?> launch
    )
    {
        // Saved first: a runner killed between Start and the PID save still reads as "a TE3 may be running".
        manifest.LaunchRequestedTicks = DateTime.UtcNow.Ticks;
        save();
        var app = launch(start) ?? throw new InvalidOperationException("TE3 did not start");
        manifest.ProcessId = app.Id;
        manifest.ProcessStartedTicks = app.StartTime.ToUniversalTime().Ticks;
        save();
        return app;
    }

    /// <summary>
    /// Runs one tool call, journaling its intent before it runs and its result or error after. The call has run by
    /// then, so a journal write that fails afterwards is reported beside the result or error, never instead of it.
    /// </summary>
    internal static async Task<McpToolResult> Tool(
        string journal,
        string tool,
        object arguments,
        Func<Task<McpToolResult>> execute
    )
    {
        Append(
            journal,
            new
            {
                phase = "intent",
                tool,
                arguments,
            }
        );
        McpToolResult result;
        try
        {
            result = await execute();
        }
        catch (Exception error)
        {
            var unrecorded = TryAppend(
                journal,
                new
                {
                    phase = "error",
                    tool,
                    error = error.Message,
                }
            );
            if (unrecorded != null)
            {
                error.Data[JournalErrorKey] = unrecorded;
            }

            throw;
        }

        var failure = TryAppend(
            journal,
            new
            {
                phase = "result",
                tool,
                result,
            }
        );
        return failure == null ? result : result with { Content = [.. result.Content, new() { Text = failure }] };
    }

    private static void Append(string journal, object entry) =>
        File.AppendAllText(journal, JsonSerializer.Serialize(entry) + Environment.NewLine);

    /// <returns>Null once written; otherwise why the entry could not be recorded.</returns>
    private static string? TryAppend(string journal, object entry)
    {
        try
        {
            Append(journal, entry);
            return null;
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or NotSupportedException or JsonException)
        {
            return $"Journal: this call's outcome could not be recorded in {journal} ({ex.Message}). The call ran.";
        }
    }
}
