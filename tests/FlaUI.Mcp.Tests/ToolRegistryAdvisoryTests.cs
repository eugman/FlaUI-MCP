using System.Text.Json;
using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Tests;

public sealed class ToolRegistryAdvisoryTests
{
    [Fact]
    public async Task RegistryInstallsMutationGrantForOneToolCall()
    {
        var registry = new ToolRegistry();
        var mutations = 0;
        var approvals = 0;
        registry.RegisterTool(new ProbeTool(() => MutationGuard.Execute(() => { }, () => mutations++)));
        registry.MutationPermissionForTool = (_, _) => () => approvals++;
        var prior = MutationGuard.BeforeMutation.Value;

        var result = await registry.ExecuteToolAsync("probe", null);

        Assert.NotEqual(true, result.IsError);
        Assert.Equal(1, mutations);
        Assert.Equal(1, approvals);
        Assert.Same(prior, MutationGuard.BeforeMutation.Value);
    }

    [Fact]
    public async Task RegistryAddsAdvisoryWarningToToolResult()
    {
        var registry = new ToolRegistry();
        registry.RegisterTool(new ProbeTool(() => { }));
        registry.AdvisoryWarning = (_, _, _) => "Outside preferred app";

        var result = await registry.ExecuteToolAsync("probe", null);

        Assert.StartsWith("Warning: Outside preferred app", result.Content[0].Text);
        Assert.Contains("probe done", result.StructuredContent!.Message);
    }

    [Fact]
    public async Task RegistryKeepsAdvisoryWarningWhenToolThrows()
    {
        var registry = new ToolRegistry();
        registry.RegisterTool(new ProbeTool(() => throw new InvalidOperationException("probe failed")));
        registry.AdvisoryWarning = (_, _, _) => "Outside preferred app";

        var result = await registry.ExecuteToolAsync("probe", null);

        Assert.True(result.IsError);
        Assert.StartsWith("Warning: Outside preferred app", result.Content[0].Text);
        Assert.Contains("probe failed", result.StructuredContent!.Message);
    }

    [Fact]
    public async Task PerToolGrantCannotReplaceAnAmbientDenial()
    {
        var registry = new ToolRegistry();
        var mutations = 0;
        registry.RegisterTool(new ProbeTool(() => MutationGuard.Execute(() => { }, () => mutations++)));
        registry.MutationPermissionForTool = (_, _) => () => { };
        var prior = MutationGuard.BeforeMutation.Value;
        MutationGuard.BeforeMutation.Value = () => throw new InvalidOperationException("ambient grant denied");
        try
        {
            var result = await registry.ExecuteToolAsync("probe", null);
            Assert.True(result.IsError);
            Assert.Contains("ambient grant denied", result.Content[0].Text);
            Assert.Equal(0, mutations);
        }
        finally
        {
            MutationGuard.BeforeMutation.Value = prior;
        }
    }

    private sealed class ProbeTool(Action run) : ToolBase
    {
        public override string Name => "probe";
        public override string Description => "Probe";
        public override object InputSchema => ToolSchema.For<NoArguments>();
        public override McpToolAnnotations Annotations => McpToolAnnotations.Input(destructive: false);

        public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
        {
            run();
            return Task.FromResult(TextResult("probe done"));
        }
    }
}
