using System.Text.Json;
using FlaUI.Mcp;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// A host's own status tools are answered while other calls block, when they declare it: read-only
/// annotations with the status hint.
/// </summary>
public sealed class StatusToolExemptionTests
{
    private static JsonRpcRequest Call(int id, string tool) =>
        JsonSerializer.Deserialize<JsonRpcRequest>(
            $$$"""{"id":{{{id}}},"method":"tools/call","params":{"name":"{{{tool}}}"}}"""
        )!;

    [Fact]
    public async Task AToolAnnotatedAsStatusIsAnsweredWhileTheSerialSlotIsHeld()
    {
        var (server, blocking) = Server(new Immediate("host_status", McpToolAnnotations.Status));
        var first = server.HandleRequestAsync(Call(1, "blocking"));
        await blocking.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            var status = await server.HandleRequestAsync(Call(2, "host_status")).WaitAsync(TimeSpan.FromSeconds(3));

            Assert.Null(status!.Error);
        }
        finally
        {
            blocking.Release.SetResult();
            await first.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task APlainReadOnlyToolStillWaitsForTheSerialSlot()
    {
        var (server, blocking) = Server(new Immediate("host_find", McpToolAnnotations.ReadOnly));
        var first = server.HandleRequestAsync(Call(1, "blocking"));
        await blocking.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var find = server.HandleRequestAsync(Call(2, "host_find"));
        try
        {
            Assert.False(find.Wait(TimeSpan.FromMilliseconds(500)));
        }
        finally
        {
            blocking.Release.SetResult();
            await first.WaitAsync(TimeSpan.FromSeconds(5));
            await find.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public void TheStatusHintIsReadOnlyAndNotSentToClients()
    {
        Assert.True(McpToolAnnotations.Status.ReadOnlyHint);
        Assert.True(McpToolAnnotations.Status.StatusHint);
        Assert.DoesNotContain(
            "status",
            JsonSerializer.Serialize(McpToolAnnotations.Status, McpProtocol.JsonOptions),
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Theory]
    [InlineData("tools/call", false)]
    [InlineData("tools/list", true)]
    public async Task AtCapacityOnlyAStatusToolCallIsAnswered(string method, bool refused)
    {
        var blocking = new Blocking();
        var registry = new ToolRegistry(TimeSpan.FromSeconds(10));
        registry.RegisterTool(blocking);
        registry.RegisterTool(new Immediate("host_status", McpToolAnnotations.Status));
        var server = new McpServer(registry, concurrentRequests: true);
        var lines = Enumerable
            .Range(1, McpLineTransport.MaxRequestsInFlight)
            .Select(id => Line(id, "tools/call", "blocking"))
            .Append(Line(99, method, "host_status"));
        var output = new StringWriter();

        var run = new McpLineTransport(server).RunAsync(
            new StringReader(string.Join("\n", lines) + "\n"),
            output,
            CancellationToken.None
        );
        await WaitFor(() => output.ToString().Contains("\"id\":99", StringComparison.Ordinal));
        blocking.Release.SetResult();
        await run.WaitAsync(TimeSpan.FromSeconds(10));

        var answer = output
            .ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => JsonDocument.Parse(l).RootElement)
            .Single(r => r.GetProperty("id").GetInt32() == 99);
        Assert.Equal(refused, answer.TryGetProperty("error", out _));
    }

    private static string Line(int id, string method, string tool) =>
        JsonSerializer.Serialize(
            new
            {
                jsonrpc = "2.0",
                id,
                method,
                @params = new { name = tool },
            }
        );

    private static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }
    }

    private static (McpServer Server, Blocking Blocking) Server(ITool status)
    {
        var blocking = new Blocking();
        var registry = new ToolRegistry(TimeSpan.FromSeconds(10));
        registry.RegisterTool(blocking);
        registry.RegisterTool(status);
        return (new McpServer(registry), blocking);
    }

    private sealed class Immediate(string name, McpToolAnnotations annotations) : ToolBase
    {
        public override string Name => name;
        public override string Description => "no desktop";
        public override object InputSchema => new { };
        public override McpToolAnnotations Annotations => annotations;

        public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments) => Task.FromResult(TextResult("ok"));
    }

    private sealed class Blocking : ToolBase
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override string Name => "blocking";
        public override string Description => "no desktop";
        public override object InputSchema => new { };

        public override async Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
        {
            Entered.TrySetResult();
            await Release.Task;
            return TextResult("done");
        }
    }
}
