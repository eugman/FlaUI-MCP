using System.Text.Json;
using FlaUI.Mcp;

/// <summary>te3_interaction_begin / te3_interaction_end: the user's grant for the TE3 tools that take the desktop.</summary>
public sealed class Te3InteractionTool(Te3Interaction interaction, string name) : ToolBase
{
    public const string Begin = "te3_interaction_begin";
    public const string End = "te3_interaction_end";
    public static readonly string[] Names = [Begin, End];

    public override string Name => name;
    public override string Description =>
        name == Begin
            ? "Begin one described batch of TE3 desktop input (te3_navigate, and te3_layout actions other than list, "
                + "plan-preset and show-pane) ONLY after the user explicitly says the machine is clear; never infer "
                + "approval from a request to do the work. The in-memory grant ends at te3_interaction_end, on any "
                + "keyboard or mouse input from the user, or when the server exits."
            : "End the TE3 interaction grant now. Sends no input and restores no focus.";
    public override object InputSchema =>
        name == Begin
            ? new
            {
                type = "object",
                properties = new
                {
                    batch = new { type = "string", description = "What will be done, as described to the user." },
                    userApproved = new
                    {
                        type = "boolean",
                        description = "True only when the user explicitly approved this batch.",
                    },
                },
                required = new[] { "batch", "userApproved" },
                additionalProperties = false,
            }
            : new
            {
                type = "object",
                properties = new { },
                additionalProperties = false,
            };

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        try
        {
            if (name == Begin)
            {
                interaction.Begin(
                    GetStringArgument(arguments, "batch") ?? "",
                    GetArgument<bool?>(arguments, "userApproved") == true
                );
            }
            else
            {
                interaction.End("ended by " + End + ".");
            }

            return Task.FromResult(TextResult(JsonSerializer.Serialize(new { active = interaction.Active })));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Task.FromResult(ErrorResult($"{name}: {ex.Message}"));
        }
    }
}
