using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using FlaUI.Mcp;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>Drives the real read/dispatch/write loop over in-memory text, as a broker sees it.</summary>
public sealed class McpLineTransportTests
{
    private static JsonRpcRequest Request(string json) => JsonSerializer.Deserialize<JsonRpcRequest>(json)!;

    private static string Call(int id, string tool = "gated") =>
        $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"method\":\"tools/call\",\"params\":{{\"name\":\"{tool}\"}}}}";

    private static async Task<bool> NotDispatched(McpServer server, string key)
    {
        var response = await server.HandleRequestAsync(
            Request($"{{\"id\":900,\"method\":\"flaui/operations/status\",\"params\":{{\"requestKey\":\"{key}\"}}}}")
        );
        return JsonSerializer.SerializeToElement(response!.Result).GetProperty("notDispatched").GetBoolean();
    }

    [Fact]
    public async Task UnparseableLineWithRecoverableIdIsAnsweredAsParseError()
    {
        var server = new McpServer(new ToolRegistry());
        var output = new LineSink();
        var input = new StringReader(
            "{\"jsonrpc\":\"2.0\",\"id\":41,\"method\":\"tools/call\",\"params\":{\"name\":}\n"
        );

        await new McpLineTransport(server).RunAsync(input, output, CancellationToken.None);

        var response = await output.NextAsync();
        Assert.Equal(41, response.GetProperty("id").GetInt32());
        Assert.Equal(-32700, response.GetProperty("error").GetProperty("code").GetInt32());
        Assert.True(response.GetProperty("error").GetProperty("data").GetProperty("notDispatched").GetBoolean());
        Assert.True(await NotDispatched(server, "41"));
    }

    [Fact]
    public async Task UnparseableLineWithoutIdIsAnsweredWithNullId()
    {
        var output = new LineSink();

        await new McpLineTransport(new McpServer(new ToolRegistry())).RunAsync(
            new StringReader("not json\n"),
            output,
            CancellationToken.None
        );

        var response = await output.NextAsync();
        Assert.Equal(JsonValueKind.Null, response.GetProperty("id").ValueKind);
        Assert.Equal(-32700, response.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task RequestBeyondCapacityIsRefusedAndRecordedButPingAndStatusStillAnswer()
    {
        var tool = new GatedTool();
        var registry = new ToolRegistry(TimeSpan.FromSeconds(30));
        registry.RegisterTool(tool);
        var server = new McpServer(registry);
        var output = new LineSink();
        var lines = Enumerable
            .Range(1, McpLineTransport.MaxRequestsInFlight)
            .Select(id => Call(id))
            .Append("{\"jsonrpc\":\"2.0\",\"id\":100,\"method\":\"ping\"}")
            .Append("{\"jsonrpc\":\"2.0\",\"id\":101,\"method\":\"flaui/operations/status\",\"params\":{}}")
            .Append(Call(33));
        var run = new McpLineTransport(server).RunAsync(
            new StringReader(string.Join("\n", lines) + "\n"),
            output,
            CancellationToken.None
        );
        try
        {
            var answered = new Dictionary<int, JsonElement>();
            while (answered.Count < 3)
            {
                var response = await output.NextAsync();
                answered[response.GetProperty("id").GetInt32()] = response;
            }

            Assert.True(answered[100].TryGetProperty("result", out _));
            Assert.True(answered[101].TryGetProperty("result", out _));
            var refused = answered[33].GetProperty("error");
            Assert.Equal(-32000, refused.GetProperty("code").GetInt32());
            Assert.True(refused.GetProperty("data").GetProperty("notDispatched").GetBoolean());
            Assert.True(await NotDispatched(server, "33"));
            Assert.False(await NotDispatched(server, "1"));
        }
        finally
        {
            tool.Release.TrySetResult();
            await run.WaitAsync(TimeSpan.FromSeconds(30));
        }

        Assert.Equal(McpLineTransport.MaxRequestsInFlight, tool.Calls);
    }

    [Fact]
    public async Task ConcurrentResponsesStayOneJsonObjectPerLine()
    {
        var registry = new ToolRegistry();
        registry.RegisterTool(new LargeTool());
        var server = new McpServer(registry, concurrentRequests: true);
        var output = new ChunkedWriter();
        var lines = Enumerable.Range(1, 30).Select(id => Call(id, "large"));

        await new McpLineTransport(server).RunAsync(
            new StringReader(string.Join("\n", lines) + "\n"),
            output,
            CancellationToken.None
        );

        var responses = output
            .ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line).RootElement)
            .ToList();
        Assert.Equal(Enumerable.Range(1, 30), responses.Select(r => r.GetProperty("id").GetInt32()).Order());
    }

    /// <summary>Collects each written line as parsed JSON.</summary>
    private sealed class LineSink : StringWriter
    {
        private readonly Channel<JsonElement> lines = Channel.CreateUnbounded<JsonElement>();

        public override Task WriteLineAsync(string? value)
        {
            lines.Writer.TryWrite(JsonDocument.Parse(value!).RootElement.Clone());
            return Task.CompletedTask;
        }

        public async Task<JsonElement> NextAsync() =>
            await lines.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
    }

    /// <summary>Yields between chunks of a line, so unsynchronized writers would interleave.</summary>
    private sealed class ChunkedWriter : StringWriter
    {
        private readonly object gate = new();

        public override async Task WriteLineAsync(string? value)
        {
            foreach (var chunk in (value ?? "").Chunk(512))
            {
                lock (gate)
                {
                    Write(chunk);
                }

                await Task.Yield();
            }

            lock (gate)
            {
                Write('\n');
            }
        }

        public override string ToString()
        {
            lock (gate)
            {
                return base.ToString();
            }
        }
    }

    private sealed class GatedTool : ToolBase
    {
        private int calls;
        public int Calls => Volatile.Read(ref calls);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override string Name => "gated";
        public override string Description => "no desktop";
        public override object InputSchema => new { };

        public override async Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
        {
            Interlocked.Increment(ref calls);
            await Release.Task;
            return TextResult("done");
        }
    }

    private sealed class LargeTool : ToolBase
    {
        public override string Name => "large";
        public override string Description => "no desktop";
        public override object InputSchema => new { };

        public override async Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
        {
            await Task.Yield();
            return TextResult(new StringBuilder().Append('x', 20000).ToString());
        }
    }
}
