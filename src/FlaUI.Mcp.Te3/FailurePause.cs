using FlaUI.Mcp;

/// <summary>
/// The pause after a TE3 tool fails part-way: a failure that sent input, or may still, leaves TE3 in a state nobody
/// has looked at, so the grant refuses further input until a read (te3_layout_read, te3_model_state) succeeds.
/// Observation tools stay allowed while paused.
/// </summary>
internal static class FailurePause
{
    /// <summary>Pauses on a failure after input, lifts on a successful observation; returns the result unchanged.</summary>
    public static McpToolResult After(Te3Interaction grant, McpToolResult result, bool observation)
    {
        if (result.IsError == true && SentOrMay(result))
        {
            grant.Pause(
                $"an earlier call failed after sending {result.Outcome?.InputsSent ?? 0} input(s), so TE3's state is unknown"
            );
        }
        else if (result.IsError != true && observation)
        {
            grant.Observed();
        }

        return result;
    }

    // A failure with no outcome, or no input count, cannot show that nothing went out.
    private static bool SentOrMay(McpToolResult result) =>
        result.Outcome is not { InputsSent: { } sent } outcome || sent > 0 || CoreFailure.MayStillGoOut(outcome);
}
