using System.Text.Json;
using FlaUI.Mcp;

/// <summary>te3_interaction_begin / te3_interaction_end: the user's grant for the TE3 tools that take the desktop.</summary>
public sealed class Te3InteractionTool(Te3Interaction interaction, string name) : ToolBase
{
    public const string Begin = "te3_interaction_begin";
    public const string End = "te3_interaction_end";
    public static readonly string[] Names = [Begin, End];

    public override string Name => name;
    public override string Description => "";
    public override object InputSchema => new { type = "object" };

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments) =>
        throw new NotImplementedException(interaction.ToString());
}
