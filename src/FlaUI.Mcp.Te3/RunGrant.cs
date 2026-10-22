using FlaUI.Mcp.Core;

/// <summary>
/// The run command's grant. Starting <c>run</c> is the operator's approval for that batch of recipes; the user's own
/// keyboard or mouse input revokes it, as in the MCP server, and the recipe fails at its next input. Core's mutation
/// grant declares the host interactive (MutationGuard.HookRequired), so input on a path without the grant installed
/// is refused, and each run path that sends input checks that first (MutationGuard.CheckPermission), before it
/// journals anything. Closing the run's own TE3 afterwards is not refused: it is
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
}
