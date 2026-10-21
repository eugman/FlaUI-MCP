using System.Text.RegularExpressions;

/// <summary>When a failed navigation may be tried again.</summary>
internal static class NavigationRetry
{
    // Core and this host end a refusal that sent nothing with "no input sent" or "sent no input".
    private static readonly Regex NoInput = new(
        @"\bno input (?:was )?sent\b|\bsent no input\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1)
    );

    /// <summary>Whether the failure says no input went out, so trying again cannot repeat any.</summary>
    internal static bool SentNoInput(Exception error) => NoInput.IsMatch(error.Message);

    /// <summary>
    /// Runs attempt, and once more only when it was refused before sending input: a failure after typing or
    /// clicking may have changed TE3, so it is reported as it is. When the retry fails too, the first error leads.
    /// </summary>
    public static async Task Once(Func<Task> attempt, Action<string>? report)
    {
        try
        {
            await attempt();
        }
        catch (Exception first) when (first is InvalidOperationException or TimeoutException && SentNoInput(first))
        {
            report?.Invoke($"Retrying after a refusal that sent no input: {first.Message}");
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
}
