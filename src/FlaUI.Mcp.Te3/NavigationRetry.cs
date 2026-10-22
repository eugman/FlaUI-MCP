using FlaUI.Mcp.Core;

/// <summary>When a failed navigation may be tried again.</summary>
internal static class NavigationRetry
{
    /// <summary>
    /// Runs attempt, and once more only when the whole attempt sent no input and Core's outcome for the failing call
    /// says it was not dispatched: a walk that typed or pressed keys may have changed TE3, so it is reported with what
    /// it sent, and a partial or pending call may have gone out. A blocked desktop (activation denied, no desktop) or
    /// a provider an earlier call still holds is not retried either: trying again cannot help. A failure Core did not report on is not retried. When the retry
    /// fails too, the first error leads.
    /// </summary>
    /// <param name="inputsSent">The caller's running count of inputs that went out.</param>
    public static async Task Once(Func<Task> attempt, Func<int> inputsSent, Action<string>? report)
    {
        var before = inputsSent();
        try
        {
            await attempt();
        }
        catch (Exception first) when (first is InvalidOperationException or AutomationRefusal or TimeoutException)
        {
            var sent = inputsSent() - before;
            if (sent > 0)
            {
                throw new InvalidOperationException(
                    $"{first.Message} This attempt sent {sent} input{(sent == 1 ? "" : "s")}, so it was not retried.",
                    first
                );
            }

            if (
                !CoreFailure.NotDispatched(first)
                || CoreFailure.Blocker(first) != null
                || CoreFailure.Outcome(first)?.Provider == "pending"
            )
            {
                throw;
            }

            report?.Invoke($"Retrying after a refusal that sent no input: {first.Message}");
            await RetryKeepingFirst(attempt, first);
        }
    }

    private static async Task RetryKeepingFirst(Func<Task> attempt, Exception first)
    {
        try
        {
            await attempt();
        }
        catch (Exception second) when (second is not OperationCanceledException)
        {
            throw new InvalidOperationException($"{first.Message} The retry also failed: {second.Message}", first);
        }
    }
}
