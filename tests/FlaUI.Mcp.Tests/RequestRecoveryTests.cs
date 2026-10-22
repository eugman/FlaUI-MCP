using System.Text.Json;
using FlaUI.Mcp;
using Xunit;

namespace FlaUI.Mcp.Tests;

public sealed class RequestRecoveryTests
{
    private static JsonRpcRequest Request(string json) => JsonSerializer.Deserialize<JsonRpcRequest>(json)!;

    [Fact]
    public async Task StatusCorrelatesAnExactRpcRequest()
    {
        var registry = new ToolRegistry();
        registry.RegisterTool(new FakeTool());
        var server = new McpServer(registry);
        await server.HandleRequestAsync(
            Request("{\"id\":23,\"method\":\"tools/call\",\"params\":{\"name\":\"fake\"}}")
        );
        await server.HandleRequestAsync(
            Request("{\"id\":25,\"method\":\"tools/call\",\"params\":{\"name\":\"fake\"}}")
        );
        var status = await server.HandleRequestAsync(
            Request("{\"id\":26,\"method\":\"flaui/operations/status\",\"params\":{\"requestKey\":\"23\"}}")
        );
        var json = JsonSerializer.SerializeToElement(status!.Result);
        var operation = Assert.Single(json.GetProperty("operations").EnumerateArray());
        Assert.Equal("23", operation.GetProperty("requestKey").GetString());
        Assert.True(operation.GetProperty("finished").GetBoolean());
        Assert.Equal("completed", operation.GetProperty("dispatch").GetString());
        Assert.False(json.GetProperty("notDispatched").GetBoolean());
    }

    [Fact]
    public async Task QueuedCancellationIsQueryableAndDoesNotInvokeAnotherTool()
    {
        var tool = new FakeTool { Block = true };
        var registry = new ToolRegistry(TimeSpan.FromSeconds(10), time: new ManualTimeProvider());
        registry.RegisterTool(tool);
        var server = new McpServer(registry);
        var first = server.HandleRequestAsync(
            Request("{\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"fake\"}}")
        );
        await tool.Entered.Task.WaitAsync(Eventually.Bound);
        try
        {
            var second = server.HandleRequestAsync(
                Request("{\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"fake\"}}")
            );
            await server.HandleRequestAsync(
                Request("{\"method\":\"notifications/cancelled\",\"params\":{\"requestId\":2}}")
            );
            Assert.Null(await second.WaitAsync(Eventually.Bound));
            var response = await server.HandleRequestAsync(
                Request("{\"id\":3,\"method\":\"flaui/operations/status\",\"params\":{\"requestKey\":\"2\"}}")
            );
            var status = JsonSerializer.SerializeToElement(response!.Result);
            Assert.True(status.GetProperty("notDispatched").GetBoolean());
            Assert.Empty(status.GetProperty("operations").EnumerateArray());
            Assert.Equal(1, tool.Calls);
        }
        finally
        {
            tool.Release.SetResult();
            await first;
        }
    }

    private sealed class FakeTool : ToolBase
    {
        public bool Block { get; init; }
        public int Calls { get; private set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override string Name => "fake";
        public override string Description => "no desktop";
        public override object InputSchema => new { };

        public override async Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
        {
            Calls++;
            Entered.TrySetResult();
            if (Block)
            {
                await Release.Task;
            }

            return TextResult("done");
        }
    }
}
