using System.Text.Json;
using FlaUI.Mcp;

/// <summary>
/// A Core tool that changes the desktop without checking Core's mutation hook, refused here unless the user's grant
/// is active. TODO(core): drop once the rebase brings Core's own focus, close and launch checks.
/// </summary>
internal sealed class GrantedTool(ITool inner, Te3Interaction interaction) : ITool
{
    public string Name => inner.Name;

    public McpTool GetDefinition()
    {
        var definition = inner.GetDefinition();
        return definition with
        {
            Description =
                definition.Description + $" In the TE3 server it requires a {Te3InteractionTool.Begin} grant.",
        };
    }

    public Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        try
        {
            interaction.Require();
        }
        catch (InvalidOperationException refusal)
        {
            return Task.FromResult(
                new McpToolResult { IsError = true, Content = [new() { Text = $"{Name}: {refusal.Message}" }] }
            );
        }

        return inner.ExecuteAsync(arguments);
    }
}
