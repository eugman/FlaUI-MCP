using System.Text.Json;
using FlaUI.Mcp;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>Protocol refusals: unknown methods, malformed calls, duplicate IDs and bounded memory.</summary>
public sealed class ProtocolBranchTests
{
    private static JsonRpcRequest Request(string json) => JsonSerializer.Deserialize<JsonRpcRequest>(json)!;

    private static JsonElement Args(object value) => JsonSerializer.SerializeToElement(value);

    private static async Task<bool> NotDispatched(McpServer server, string key)
    {
        var response = await server.HandleRequestAsync(
            Request($"{{\"id\":900,\"method\":\"flaui/operations/status\",\"params\":{{\"requestKey\":\"{key}\"}}}}")
        );
        return JsonSerializer.SerializeToElement(response!.Result).GetProperty("notDispatched").GetBoolean();
    }

    [Fact]
    public async Task AnUnknownMethodIsAJsonRpcError()
    {
        var server = new McpServer(new ToolRegistry());

        var response = await server.HandleRequestAsync(Request("{\"id\":3,\"method\":\"tools/destroy\"}"));

        Assert.Null(response!.Result);
        Assert.Equal(-32603, response.Error!.Code);
        Assert.Equal("Unknown method: tools/destroy", response.Error.Message);
    }

    [Fact]
    public async Task AToolCallWithoutParamsIsRefused()
    {
        var server = new McpServer(new ToolRegistry());

        var response = await server.HandleRequestAsync(Request("{\"id\":4,\"method\":\"tools/call\"}"));

        var result = Assert.IsType<McpToolResult>(response!.Result);
        Assert.True(result.IsError);
        Assert.Equal("Missing params", result.Content[0].Text);
    }

    [Fact]
    public async Task ADuplicateInFlightRequestIdIsRefusedWithoutDispatch()
    {
        var tool = new BlockingTool();
        var registry = new ToolRegistry(TimeSpan.FromSeconds(10));
        registry.RegisterTool(tool);
        var server = new McpServer(registry, concurrentRequests: true);
        const string call = "{\"id\":7,\"method\":\"tools/call\",\"params\":{\"name\":\"blocking\"}}";

        var first = server.HandleRequestAsync(Request(call));
        await tool.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var duplicate = await server.HandleRequestAsync(Request(call));
        tool.Release.SetResult();
        await first.WaitAsync(TimeSpan.FromSeconds(5));

        var refused = Assert.IsType<McpToolResult>(duplicate!.Result);
        Assert.True(refused.IsError);
        Assert.Equal("Duplicate in-flight request ID; no action dispatched.", refused.Content[0].Text);
        Assert.Equal(1, tool.Calls);
    }

    [Fact]
    public async Task OnlyTheNewest128NotDispatchedRequestsAreRemembered()
    {
        var server = new McpServer(new ToolRegistry());
        for (var key = 0; key <= 128; key++)
        {
            server.RecordNotDispatched(key.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        Assert.False(await NotDispatched(server, "0"));
        Assert.True(await NotDispatched(server, "1"));
        Assert.True(await NotDispatched(server, "128"));
    }

    [Fact]
    public async Task BlankLinesAreSkipped()
    {
        var server = new McpServer(new ToolRegistry());
        var output = new StringWriter();
        var input = new StringReader("\n   \n{\"jsonrpc\":\"2.0\",\"id\":5,\"method\":\"ping\"}\n\t\n");

        await new McpLineTransport(server).RunAsync(input, output, CancellationToken.None);

        var lines = output
            .ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var response = JsonDocument.Parse(Assert.Single(lines)).RootElement;
        Assert.Equal(5, response.GetProperty("id").GetInt32());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ANonPositiveToolTimeoutIsRejected(int seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ToolRegistry(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public async Task ABatchMissingActionsIsRefused()
    {
        var registry = new ToolRegistry();
        registry.RegisterTool(new BatchTool(registry));

        var result = await registry.ExecuteToolAsync("windows_batch", Args(new { }));

        Assert.True(result.IsError);
        Assert.Contains("Missing actions", result.Content[0].Text);
    }

    [Theory]
    [InlineData(100, false)]
    [InlineData(101, true)]
    public async Task ABatchHoldsAtMost100Steps(int count, bool refused)
    {
        var registry = new ToolRegistry();
        registry.RegisterTool(new BatchTool(registry));
        var actions = Enumerable.Range(0, count).Select(_ => new { action = "wait", ms = 0 }).ToArray();

        var result = await registry.ExecuteToolAsync("windows_batch", Args(new { actions }));

        Assert.Equal(refused, result.IsError == true);
        Assert.Equal(refused, result.Content[0].Text!.Contains("At most 100 steps per batch"));
    }

    [Fact]
    public async Task ANegativeWaitStopsTheBatch()
    {
        var rows = Rows(new { action = "wait", ms = -1 }, new { action = "click", @ref = "w1e1" });
        var calls = 0;

        var result = await BatchTool.ExecuteRows(rows, (_, _) => Count(ref calls));

        Assert.Equal("failed", result.Outcome!.Status);
        Assert.Contains("1. ERROR: Wait must be nonnegative", result.Content[0].Text);
        Assert.Contains("Stopped at action 1 due to error", result.Content[0].Text);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task AWaitStepIsRecordedAsCompleted()
    {
        var result = await BatchTool.ExecuteRows(Rows(new { action = "wait", ms = 0 }), (_, _) => Fail());

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("1. wait: Waited 0ms", result.Content[0].Text);
        var step = Assert.Single(result.Outcome!.Steps!);
        Assert.Equal("wait", step.Action);
        Assert.False(step.IsError);
    }

    [Fact]
    public async Task AnUnknownActionIsAnErrorStep()
    {
        var result = await BatchTool.ExecuteRows(Rows(new { action = "zap" }), (_, _) => Fail());

        Assert.Equal("failed", result.Outcome!.Status);
        Assert.Contains("1. ERROR: Unknown action: zap", result.Content[0].Text);
    }

    [Fact]
    public async Task WithoutStopOnErrorTheBatchContinuesPastAFailedStep()
    {
        var rows = Rows(new { action = "zap" }, new { action = "click", @ref = "w1e1" });
        var calls = 0;

        var result = await BatchTool.ExecuteRows(rows, (_, _) => Count(ref calls), stopOnError: false);

        Assert.Equal("failed", result.Outcome!.Status);
        Assert.Equal(1, calls);
        Assert.DoesNotContain("Stopped", result.Content[0].Text);
        Assert.Equal(2, result.Outcome!.Steps!.Count);
    }

    [Fact]
    public async Task AStepWhoseToolThrowsIsReportedAndStopsTheBatch()
    {
        var registry = new ToolRegistry();
        registry.RegisterTool(new ThrowingTool());
        registry.RegisterTool(new BatchTool(registry));
        var actions = new object[] { new { action = "click", @ref = "w1e1" }, new { action = "wait", ms = 0 } };

        var result = await registry.ExecuteToolAsync("windows_batch", Args(new { actions }));

        Assert.Equal("failed", result.Outcome!.Status);
        Assert.Contains("1. click: Error: boom", result.Content[0].Text);
        Assert.Contains("Stopped at action 1 due to error", result.Content[0].Text);
        Assert.DoesNotContain("2. wait", result.Content[0].Text);
    }

    private static JsonElement[] Rows(params object[] rows) => rows.Select(row => Args(row)).ToArray();

    private static Task<McpToolResult> Count(ref int calls)
    {
        calls++;
        return Task.FromResult(new McpToolResult { Content = [new() { Type = "text", Text = "clicked" }] });
    }

    private static Task<McpToolResult> Fail() => throw new InvalidOperationException("no tool should run");

    private sealed class BlockingTool : ToolBase
    {
        public int Calls { get; private set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override string Name => "blocking";
        public override string Description => "no desktop";
        public override object InputSchema => new { };

        public override async Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
        {
            Calls++;
            Entered.TrySetResult();
            await Release.Task;
            return TextResult("done");
        }
    }

    private sealed class ThrowingTool : ToolBase
    {
        public override string Name => "windows_click";
        public override string Description => "no desktop";
        public override object InputSchema => new { };

        public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments) =>
            throw new InvalidOperationException("boom");
    }
}
