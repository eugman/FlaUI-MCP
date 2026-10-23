using System.Text.Json;
using FlaUI.Mcp.Core;

namespace FlaUI.Mcp;

/// <summary>The result of a call that returned before its tool did: a timeout or a cancel.</summary>
public partial class ToolRegistry
{
    /// <summary>
    /// Result for a call that returned before its tool task ended: the caller cancelled (<paramref name="settled"/>
    /// is set) or the tool timed out. The tool may still be running and may have partially executed.
    /// </summary>
    private McpToolResult Strand(
        string name,
        OperationContext operation,
        Task<McpToolResult> toolTask,
        Task? settled,
        Action<Task>? whenStranded
    )
    {
        operation.Stop.Cancel();
        if (settled != null)
        {
            whenStranded?.Invoke(settled);
        }

        // A stranded call can still hold this app's UIA provider. Fail later ref tools fast until it returns.
        if (operation.ProcessId != 0)
        {
            TrackStranded(Pending, name, operation.ProcessId, settled != null, toolTask);
            if (SecondaryPending != Pending)
            {
                TrackStranded(SecondaryPending, name, operation.ProcessId, settled != null, toolTask);
            }
        }

        _ = toolTask.ContinueWith(
            task =>
            {
                _ = task.Exception;
            },
            TaskContinuationOptions.OnlyOnFaulted
        );

        return WithAdvisory(
            new McpToolResult
            {
                Content = new List<McpContent>
                {
                    new() { Type = "text", Text = PendingMessage(name, settled != null) },
                },
                // Failed like any error result, and carrying a refusal the call recorded before it stranded.
                Outcome = new ToolOutcome("timed_out_pending", "unknown", OperationId: operation.Id)
                {
                    InputsSent = operation.InputsSent,
                    Status = "failed",
                    Failure = operation.Refusal is { } refusal
                        ? refusal with
                        {
                            ActionDispatched = refusal.ActionDispatched == true || operation.InputsSent > 0,
                        }
                        : null,
                },
                IsError = true,
            },
            Volatile.Read(ref operation.AdvisoryWarning)
        );
    }

    private static void TrackStranded(
        PendingInvokeTracker? tracker,
        string name,
        int processId,
        bool cancelled,
        Task<McpToolResult> toolTask
    )
    {
        if (tracker == null)
        {
            return;
        }

        var stranded = tracker.Begin(processId, cancelled ? $"cancelled {name}" : $"timed-out {name}");
        _ = toolTask.ContinueWith(_ => tracker.Complete(stranded), TaskScheduler.Default);
    }

    /// <summary>Completes when the tool returns, or when its timeout would have expired had it not been cancelled.</summary>
    private Task SettledOrTimedOut(Task toolTask, TimeSpan elapsed)
    {
        var remaining = _toolTimeout - elapsed;
        return Task.WhenAny(toolTask, Task.Delay(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero, _time));
    }

    private string PendingMessage(string name, bool cancelled) =>
        (
            cancelled
                ? $"Tool '{name}' cancellation requested. "
                : $"Tool '{name}' timed out after {(int)_toolTimeout.TotalMilliseconds}ms. "
        )
        + "A modal dialog or blocked UI Automation provider may still be running in the background. "
        + "Inspect the target and operation status before continuing. The request may have partially executed; do not blindly replay it.";
}
