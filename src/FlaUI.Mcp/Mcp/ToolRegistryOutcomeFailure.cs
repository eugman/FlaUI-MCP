using FlaUI.Mcp.Core;

namespace FlaUI.Mcp;

public partial class ToolRegistry
{
    /// <summary>Keep a typed refusal where one exists; classify only a settled, untyped zero-input failure.</summary>
    private static InputRefusal? FailureFor(
        McpToolResult result,
        ToolOutcome outcome,
        InputRefusal? refusal,
        int inputsSent,
        bool providerPending,
        bool failed
    )
    {
        if (failed && refusal != null)
        {
            // A refusal recorded as coming after input keeps that evidence, whatever this call counted.
            return refusal with { ActionDispatched = refusal.ActionDispatched == true || inputsSent > 0 };
        }

        if (!failed || outcome.Failure != null || inputsSent != 0)
        {
            return outcome.Failure;
        }

        if (outcome.Dispatch != "not-dispatched" || outcome.Provider == "pending" || providerPending)
        {
            return null;
        }

        // Every mutation records its dispatch. A settled no-input failure can tell a broker it did not act.
        var message = string.Join("; ", result.Content.Select(content => content.Text));
        return new InputRefusal(RefusalCodes.OperationFailed, message) { ActionDispatched = false };
    }
}
