using System.Diagnostics;
using System.Text.Json;
using FlaUI.Mcp;

/// <summary>What a run records before it acts, so a runner killed mid-step still leaves evidence of it.</summary>
internal static class RunJournal
{
    /// <summary>Starts TE3 and records the owned PID and start time.</summary>
    internal static Process Launch(
        RunManifest manifest,
        Action save,
        ProcessStartInfo start,
        Func<ProcessStartInfo, Process?> launch
    )
    {
        var app = launch(start) ?? throw new InvalidOperationException("TE3 did not start");
        manifest.ProcessId = app.Id;
        manifest.ProcessStartedTicks = app.StartTime.ToUniversalTime().Ticks;
        save();
        return app;
    }

    /// <summary>Runs one tool call and appends it to the run's action journal.</summary>
    internal static async Task<McpToolResult> Tool(
        string journal,
        string tool,
        object arguments,
        Func<Task<McpToolResult>> execute
    )
    {
        var result = await execute();
        File.AppendAllText(
            journal,
            JsonSerializer.Serialize(
                new
                {
                    tool,
                    arguments,
                    result,
                }
            ) + Environment.NewLine
        );
        return result;
    }
}
