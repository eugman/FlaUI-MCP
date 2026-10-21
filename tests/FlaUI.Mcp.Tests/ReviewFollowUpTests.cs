using System.Text.Json;
using FlaUI.Mcp;
using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>Dispatch reporting fixed after the core stack review.</summary>
public sealed class ReviewFollowUpTests
{
    private static JsonElement Args(object value) => JsonSerializer.SerializeToElement(value);

    [Fact]
    public async Task ABatchBlockedAfterEarlierInputIsPartialAndStillPending()
    {
        var registry = new ToolRegistry();
        registry.RegisterTool(new Sends("windows_type"));
        registry.RegisterTool(new Blocked("windows_click"));
        registry.RegisterTool(new BatchTool(registry));
        var actions = new object[] { new { action = "type", text = "a" }, new { action = "click", @ref = "w1e1" } };

        var result = await registry.ExecuteToolAsync("windows_batch", Args(new { actions }));

        Assert.Equal("partial", result.Outcome!.Dispatch);
        Assert.Equal(1, result.Outcome.InputsSent);
        Assert.True(result.Outcome.IsPending);
    }

    [Fact]
    public async Task ABatchBlockedBeforeAnyInputIsStillNotDispatched()
    {
        var registry = new ToolRegistry();
        registry.RegisterTool(new Blocked("windows_click"));
        registry.RegisterTool(new BatchTool(registry));
        var actions = new object[] { new { action = "click", @ref = "w1e1" } };

        var result = await registry.ExecuteToolAsync("windows_batch", Args(new { actions }));

        Assert.Equal("not-dispatched", result.Outcome!.Dispatch);
        Assert.True(result.Outcome.IsPending);
    }

    private sealed class Sends(string name) : ToolBase
    {
        public override string Name => name;
        public override string Description => "no desktop";
        public override object InputSchema => new { };

        public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
        {
            MutationGuard.RecordDispatch();
            return Task.FromResult(TextResult("typed"));
        }
    }

    private sealed class Blocked(string name) : ToolBase
    {
        public override string Name => name;
        public override string Description => "no desktop";
        public override object InputSchema => new { };

        public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments) =>
            Task.FromResult(BlockedResult(new PendingInvokeTracker(_ => 1).Begin(42, "Invoke on 'Open...'")));
    }
}
