/// <summary>When a failed navigation may be tried again.</summary>
internal static class NavigationRetry
{
    /// <summary>Runs attempt, and once more after it fails.</summary>
    public static async Task Once(Func<Task> attempt, Action<string>? report)
    {
        Exception? first = null;
        // Navigation edits nothing, so one clean retry is safe when a check fails on a slow redraw.
        for (var tries = 0; tries < 2; tries++)
        {
            try
            {
                await attempt();
                return;
            }
            catch (InvalidOperationException ex) when (tries == 0)
            {
                first = ex;
                report?.Invoke($"Retrying from a clean tree after: {ex.Message}");
            }
        }

        throw first!;
    }
}
