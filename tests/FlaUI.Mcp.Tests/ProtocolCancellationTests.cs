using System.Text.Json;
using FlaUI.Mcp;
using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Tests;

public sealed class ProtocolCancellationTests
{
    private static JsonRpcRequest Request(string json) => JsonSerializer.Deserialize<JsonRpcRequest>(json)!;

    [Theory]
    [InlineData("2024-11-05", "2024-11-05")]
    [InlineData("2025-03-26", "2025-03-26")]
    [InlineData("unknown", "2024-11-05")]
    public async Task NegotiationAdvertisesOnlySupportedVersion(string requested, string expected)
    {
        var server = new McpServer(new ToolRegistry());
        var response = await server.HandleRequestAsync(
            Request($"{{\"id\":1,\"method\":\"initialize\",\"params\":{{\"protocolVersion\":\"{requested}\"}}}}")
        );
        Assert.Equal(expected, Assert.IsType<McpInitializeResult>(response!.Result).ProtocolVersion);
    }

    [Theory]
    [InlineData("{\"method\":\"unknown\"}")]
    [InlineData("{\"method\":\"tools/call\",\"params\":{\"name\":\"fake\"}}")]
    [InlineData("{\"method\":\"notifications/cancelled\",\"params\":{\"requestId\":99}}")]
    public async Task NotificationsNeverDispatchToolsOrReturnResponses(string json)
    {
        var server = new McpServer(new ToolRegistry());
        Assert.Null(await server.HandleRequestAsync(Request(json)));
    }

    [Fact]
    public async Task CancellationSuppressesResponseAndPreventsLateMutation()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tool = new FakeTool(entered, release, ended);
        var registry = new ToolRegistry(TimeSpan.FromSeconds(10));
        registry.RegisterTool(tool);
        var server = new McpServer(registry);
        var call = server.HandleRequestAsync(
            Request("{\"id\":7,\"method\":\"tools/call\",\"params\":{\"name\":\"fake\"}}")
        );
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(
            await server.HandleRequestAsync(
                Request("{\"method\":\"notifications/cancelled\",\"params\":{\"requestId\":7}}")
            )
        );
        Assert.Null(await call.WaitAsync(TimeSpan.FromSeconds(5)));
        release.SetResult();
        await ended.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(tool.Mutated);
    }

    [Fact]
    public async Task SuccessfulCallsHaveStableOperationIdentityWithoutClaimingVerification()
    {
        var registry = new ToolRegistry();
        registry.RegisterTool(new ImmediateTool());
        var result = await registry.ExecuteToolAsync("immediate", null);
        Assert.False(string.IsNullOrWhiteSpace(result.Outcome!.OperationId));
        Assert.Equal("not-established", result.Outcome.Verification);
        Assert.Contains(result.Outcome.OperationId!, JsonSerializer.Serialize(registry.Operations.Status()));
    }

    private sealed class ImmediateTool : ToolBase
    {
        public override string Name => "immediate";
        public override string Description => "no desktop";
        public override object InputSchema => new { };

        public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments) =>
            Task.FromResult(TextResult("accepted"));
    }

    private sealed class FakeTool(
        TaskCompletionSource entered,
        TaskCompletionSource release,
        TaskCompletionSource ended
    ) : ToolBase
    {
        public bool Mutated { get; private set; }
        public override string Name => "fake";
        public override string Description => "no desktop";
        public override object InputSchema => new { };

        public override async Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
        {
            entered.SetResult();
            try
            {
                await release.Task;
                OperationContext.Check();
                Mutated = true;
                return TextResult("done");
            }
            finally
            {
                ended.SetResult();
            }
        }
    }
}
