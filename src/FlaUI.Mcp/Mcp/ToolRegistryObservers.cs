using System.Text.Json;
using FlaUI.Mcp.Core;

namespace FlaUI.Mcp;

public partial class ToolRegistry
{
    private McpToolResult CompleteAndObserve(
        string name,
        JsonElement? arguments,
        OperationContext operation,
        McpToolResult result
    )
    {
        var completed = CompleteResult(result, operation);
        var observed = completed.Outcome;
        if (IsSettledObservation(completed, operation))
        {
            try
            {
                ObservedAfterSuccess?.Invoke(name, arguments, observed, IdentityOf(operation));
            }
            catch
            {
                // A recovery observer must not change the already-settled tool result.
            }
        }

        return completed;
    }

    private bool IsSettledObservation(McpToolResult result, OperationContext operation)
    {
        if (result.IsError == true)
        {
            return false;
        }

        var outcome = result.Outcome;
        return outcome?.Status == "ok"
            && outcome.InputsSent == 0
            && outcome.Provider != "pending"
            && !ProviderPending(operation);
    }

    private McpToolResult FinalizeOperation(string name, OperationContext operation, McpToolResult result)
    {
        operation.ResultOutcome = result.Outcome;
        if (result.Outcome!.Status == "failed")
        {
            var error = string.Join("; ", result.Content.Select(c => c.Text));
            operation.Error = string.IsNullOrWhiteSpace(error) ? "Tool reported a failure." : error;
        }

        operation.Finished = true;
        NotifyFailedAfterInput(name, operation, FailureToNotify(operation, result.Outcome));
        return result;
    }

    private static ToolOutcome FailureToNotify(OperationContext operation, ToolOutcome outcome) =>
        operation.Stranded && operation.InputsSent > 0 && outcome.Status != "failed"
            ? new ToolOutcome("timed_out_pending", "unknown", OperationId: operation.Id)
            {
                Status = "failed",
                InputsSent = operation.InputsSent,
            }
            : outcome;

    private void NotifyFailedAfterInput(string name, OperationContext operation, ToolOutcome? outcome)
    {
        if (outcome is not { Status: "failed" } failed)
        {
            return;
        }

        if (
            failed.InputsSent == 0
            && failed.Failure?.ActionDispatched != true
            && failed.Dispatch is not ("partial" or "completed")
        )
        {
            return;
        }

        if (!operation.TryNotifyFailure())
        {
            return;
        }

        try
        {
            FailedAfterInput?.Invoke(name, failed);
        }
        catch
        {
            // Legacy observers cannot suppress the target-aware pause.
        }

        try
        {
            FailedAfterInputWithTarget?.Invoke(name, failed, IdentityOf(operation));
        }
        catch
        {
            // Grant observers cannot rewrite a settled failure.
        }
    }

    private static ProcessIdentity? IdentityOf(OperationContext operation) =>
        operation.ProcessId == 0 ? null : new ProcessIdentity(operation.ProcessId, operation.ProcessStartedTicks);
}
