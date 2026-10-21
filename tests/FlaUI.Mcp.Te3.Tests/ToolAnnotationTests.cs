using System.Text.Json;
using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>
/// Every TE3 tool tells a client, through MCP annotations, whether a failed call was an observation (safe to repeat)
/// or a possible change (inspect first). Builds the server's registry; nothing is sent.
/// </summary>
public sealed class ToolAnnotationTests : IDisposable
{
    private readonly AutomationHost host = new(new ProcessPolicy(["TabularEditor3"]), includeDesktopTools: true);
    private readonly Te3Interaction interaction = new(() => 0);

    public ToolAnnotationTests() => Te3McpServer.Install(host, interaction);

    public void Dispose()
    {
        MutationGuard.BeforeMutation.Value = null;
        interaction.Dispose();
        host.Dispose();
    }

    private McpTool Tool(string name) => host.Tools.GetToolDefinitions().Single(t => t.Name == name);

    [Fact]
    public void EveryTe3ToolHasAnnotations()
    {
        var unannotated = host
            .Tools.GetToolDefinitions()
            .Where(t => t.Name.StartsWith("te3_", StringComparison.Ordinal) && t.Annotations == null)
            .Select(t => t.Name);

        Assert.Empty(unannotated);
    }

    [Theory]
    [InlineData("te3_model_state")]
    [InlineData("te3_native_preflight")]
    [InlineData("te3_layout_read")]
    public void ObservationsAreReadOnly(string name) =>
        Assert.Equal(McpToolAnnotations.ReadOnly, Tool(name).Annotations);

    [Theory]
    [InlineData("te3_navigate", false)]
    [InlineData("te3_layout", true)]
    [InlineData("te3_interaction_begin", false)]
    public void ToolsThatChangeTheDesktopOrTheGrantAreNotReadOnly(string name, bool destructive)
    {
        var annotations = Tool(name).Annotations!;

        Assert.False(annotations.ReadOnlyHint);
        Assert.Equal(destructive, annotations.DestructiveHint);
    }

    private static string[] Actions(McpTool tool) =>
        JsonSerializer
            .SerializeToElement(tool.InputSchema)
            .GetProperty("properties")
            .GetProperty("action")
            .GetProperty("enum")
            .EnumerateArray()
            .Select(a => a.GetString()!)
            .ToArray();

    [Fact]
    public void TheLayoutToolsSplitReadsFromChanges()
    {
        Assert.Equal(["list", "plan-preset", "show-pane"], Actions(Tool("te3_layout_read")));
        Assert.Empty(Actions(Tool("te3_layout")).Intersect(["list", "plan-preset", "show-pane"]));
        Assert.Contains("resize", Actions(Tool("te3_layout")));
    }

    [Fact]
    public async Task TheChangingLayoutToolSendsAReadToTheReadOnlyOne()
    {
        var result = await host.Tools.ExecuteToolAsync(
            "te3_layout",
            JsonSerializer.SerializeToElement(new { action = "list" })
        );

        Assert.True(result.IsError);
        Assert.Contains("te3_layout_read", result.Content[0].Text);
        Assert.Equal("not-dispatched", result.Outcome?.Dispatch);
    }
}
