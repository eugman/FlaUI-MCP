using FlaUI.Mcp.Core;

/// <summary>
/// The run command's grant. Starting <c>run</c> is the operator's approval for that batch of recipes; the user's own
/// keyboard or mouse input revokes it, as in the MCP server, and the recipe fails at its next input. Core's mutation
/// hook allows everything while nothing sets it (TODO(core): fail closed), so each run path that sends input first
/// checks that the run's grant is installed on its flow. Closing the run's own TE3 afterwards is not refused: it is
/// how the run restores the user's preferences.
/// </summary>
internal static class RunGrant
{
    /// <summary>Begins the grant for the recipes the operator named; disposes it if the observer cannot start.</summary>
    public static Te3Interaction Begin(Te3Interaction interaction, IEnumerable<string> recipes)
    {
        try
        {
            interaction.Begin("FlaUI.Mcp.Te3 run " + string.Join(", ", recipes), userApproved: true);
            return interaction;
        }
        catch
        {
            interaction.Dispose();
            throw;
        }
    }

    /// <summary>Throws, sending nothing, unless a Te3Interaction grant is installed here and still holds.</summary>
    public static void RequireInstalled()
    {
        var hook = MutationGuard.BeforeMutation.Value;
        if (hook?.Target is not Te3Interaction)
        {
            throw new InvalidOperationException(
                "The run sends input only under its own grant (Te3Interaction.Guard), and none is installed here. "
                    + "Nothing was sent."
            );
        }

        hook();
    }
}
