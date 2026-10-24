using System.Text.Json;
using FlaUI.Mcp;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// A closed schema (additionalProperties: false) is enforced: a misspelled or invented argument is refused
/// before the tool runs, by name, instead of being silently ignored.
/// </summary>
public sealed class ClosedSchemaTests
{
    private static JsonElement Args(object value) => JsonSerializer.SerializeToElement(value);

    private sealed class Probe(bool closed, string name = "probe") : ToolBase
    {
        public int Runs { get; private set; }
        public override string Name => name;
        public override string Description => "no desktop";

        public override object InputSchema =>
            closed
                ? new
                {
                    type = "object",
                    additionalProperties = false,
                    properties = new { @ref = new { type = "string" } },
                }
                : new { type = "object", properties = new { @ref = new { type = "string" } } };

        public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
        {
            Runs++;
            return Task.FromResult(TextResult("ran"));
        }
    }

    [Fact]
    public async Task AnUndeclaredArgumentIsRefusedByNameBeforeTheToolRuns()
    {
        var probe = new Probe(closed: true);
        var registry = new ToolRegistry();
        registry.RegisterTool(probe);

        var result = await registry.ExecuteToolAsync("probe", Args(new { @ref = "w1e1", verifyFocuss = true }));

        Assert.True(result.IsError);
        Assert.Equal(
            "Unknown argument 'verifyFocuss' for probe; no input sent. Declared arguments: ref.",
            result.Content[0].Text
        );
        Assert.Equal("not-dispatched", result.Outcome!.Dispatch);
        Assert.Equal("invalid-arguments", result.Outcome.Failure?.Code);
        Assert.False(result.Outcome.Failure?.ActionDispatched);
        Assert.Equal(0, probe.Runs);
    }

    [Fact]
    public async Task DeclaredArgumentsRun()
    {
        var probe = new Probe(closed: true);
        var registry = new ToolRegistry();
        registry.RegisterTool(probe);

        var result = await registry.ExecuteToolAsync("probe", Args(new { @ref = "w1e1" }));

        Assert.NotEqual(true, result.IsError);
        Assert.Equal(1, probe.Runs);
    }

    [Fact]
    public async Task AnOpenSchemaStillTakesExtraArguments()
    {
        var probe = new Probe(closed: false);
        var registry = new ToolRegistry();
        registry.RegisterTool(probe);

        await registry.ExecuteToolAsync("probe", Args(new { @ref = "w1e1", extra = 1 }));

        Assert.Equal(1, probe.Runs);
    }

    [Fact]
    public async Task ABatchRowPassesWithoutItsActionAndAMisspelledKeyStopsIt()
    {
        var click = new Probe(closed: true, name: "windows_click");
        var registry = new ToolRegistry();
        registry.RegisterTool(click);
        registry.RegisterTool(new FlaUI.Mcp.Tools.BatchTool(registry));

        var ok = await registry.ExecuteToolAsync(
            "windows_batch",
            Args(new { actions = new object[] { new { action = "click", @ref = "w1e1" } } })
        );
        var misspelled = await registry.ExecuteToolAsync(
            "windows_batch",
            Args(new { actions = new object[] { new { action = "click", rf = "w1e1" } } })
        );

        Assert.NotEqual(true, ok.IsError);
        Assert.True(misspelled.IsError);
        Assert.Contains("Unknown argument 'rf' for windows_click", misspelled.Content[0].Text);
        Assert.Equal(1, click.Runs);
    }
}
