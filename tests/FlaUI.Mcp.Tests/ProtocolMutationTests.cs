using System.Diagnostics;
using System.Text.Json;
using FlaUI.Mcp;
using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>Tests written to kill mutants that survived in the MCP server, transport and tool registry.</summary>
public sealed class ProtocolMutationTests
{
    private static JsonRpcRequest Request(string json) => JsonSerializer.Deserialize<JsonRpcRequest>(json)!;

    private static string Call(int id, string tool) =>
        $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"method\":\"tools/call\",\"params\":{{\"name\":\"{tool}\"}}}}";

    private static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value, McpProtocol.JsonOptions);

    private static JsonElement[] Lines(StringWriter output) =>
        output
            .ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => JsonDocument.Parse(line).RootElement.Clone())
            .ToArray();

    [Fact]
    public async Task RefusedLinesSayNothingWasDispatched()
    {
        var server = new McpServer(new ToolRegistry());
        var output = new LockedOutput();

        await new McpLineTransport(server).RunAsync(new StringReader("{\"id\":4,\n"), output, CancellationToken.None);

        var error = Assert.Single(Lines(output)).GetProperty("error");
        Assert.Equal("Parse error; no action dispatched.", error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task FinishedRequestsFreeTheirCapacity()
    {
        var registry = new ToolRegistry();
        registry.RegisterTool(new Immediate("fast"));
        var server = new McpServer(registry);
        var calls = Enumerable.Range(1, McpLineTransport.MaxRequestsInFlight + 8).Select(id => Call(id, "fast"));
        var output = new LockedOutput();

        await new McpLineTransport(server).RunAsync(new PacedReader(calls, output), output, CancellationToken.None);

        var responses = Lines(output);
        Assert.Equal(McpLineTransport.MaxRequestsInFlight + 8, responses.Length);
        Assert.All(responses, response => Assert.False(response.TryGetProperty("error", out _)));
    }

    [Fact]
    public async Task ACapacityRefusalSaysNothingWasDispatched()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var registry = new ToolRegistry(TimeSpan.FromSeconds(10), time: new ManualTimeProvider());
        registry.RegisterTool(new Gated(gate.Task));
        var server = new McpServer(registry, concurrentRequests: true);
        var calls = Enumerable.Range(1, McpLineTransport.MaxRequestsInFlight + 1).Select(id => Call(id, "gated"));
        var output = new LockedOutput();

        var run = new McpLineTransport(server).RunAsync(
            new StringReader(string.Join("\n", calls) + "\n"),
            output,
            CancellationToken.None
        );
        await Eventually.WaitUntilAsync(
            () => output.ToString().Contains("capacity", StringComparison.Ordinal),
            "the response"
        );
        gate.SetResult();
        await run.WaitAsync(Eventually.Bound);

        var refusal = Lines(output).Single(line => line.TryGetProperty("error", out _));
        Assert.Equal(
            "Request capacity reached; no action dispatched.",
            refusal.GetProperty("error").GetProperty("message").GetString()
        );
    }

    [Theory]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":7,\"method\":", 7)]
    [InlineData("{\"jsonrpc\":\"2.0\",\"params\":{\"id\":5},\"id\":7,\"method\":", 7)]
    public void TheTopLevelIdIsRecovered(string line, int id)
    {
        Assert.Equal(id, McpLineTransport.RecoverId(line)!.Value.GetInt32());
    }

    [Fact]
    public async Task InitializeAdvertisesTheServerAndItsCapabilities()
    {
        var server = new McpServer(new ToolRegistry(), "test-server");

        var response = await server.HandleRequestAsync(Request("{\"id\":1,\"method\":\"initialize\"}"));

        var result = Json(response!.Result);
        var capabilities = result.GetProperty("capabilities");
        Assert.False(capabilities.GetProperty("tools").GetProperty("listChanged").GetBoolean());
        Assert.Equal(
            1,
            capabilities
                .GetProperty("experimental")
                .GetProperty("flauiOperationStatus")
                .GetProperty("version")
                .GetInt32()
        );
        Assert.Equal("test-server", result.GetProperty("serverInfo").GetProperty("name").GetString());
        Assert.Equal("0.1.0", result.GetProperty("serverInfo").GetProperty("version").GetString());
    }

    [Fact]
    public async Task SerialCallsReturnTheirResultsAndFreeTheSlot()
    {
        var registry = new ToolRegistry();
        registry.RegisterTool(new Immediate("fast"));
        var server = new McpServer(registry);

        var first = await server.HandleRequestAsync(Request(Call(1, "fast")));
        var second = await server.HandleRequestAsync(Request(Call(1, "fast")));

        Assert.Null(first!.Error);
        Assert.Equal("done", Assert.IsType<McpToolResult>(first.Result).Content[0].Text);
        Assert.Null(second!.Error);
        Assert.Equal("done", Assert.IsType<McpToolResult>(second.Result).Content[0].Text);
        Assert.False(server.SerialSlotHeld);
    }

    [Fact]
    public async Task ErrorResultsAreText()
    {
        var server = new McpServer(new ToolRegistry());
        var registry = new ToolRegistry();

        var missingParams = await server.HandleRequestAsync(Request("{\"id\":4,\"method\":\"tools/call\"}"));
        var unknown = await registry.ExecuteToolAsync("nope", null);

        Assert.Equal("text", Assert.IsType<McpToolResult>(missingParams!.Result).Content[0].Type);
        Assert.True(unknown.IsError);
        Assert.Equal("text", unknown.Content[0].Type);
        Assert.Equal("Unknown tool: nope", unknown.Content[0].Text);
    }

    [Fact]
    public async Task ResultsCarryTextAndImageTypes()
    {
        var registry = new ToolRegistry();
        registry.RegisterTool(new Immediate("fast"));
        registry.RegisterTool(new Picture());
        registry.RegisterTool(new Throws(new InvalidOperationException("boom")));

        var text = await registry.ExecuteToolAsync("fast", null);
        var image = await registry.ExecuteToolAsync("picture", null);
        var failed = await registry.ExecuteToolAsync("throws", null);

        Assert.Equal("text", text.Content[0].Type);
        Assert.Equal("ok", text.Outcome!.Status);
        Assert.Equal("not-tracked", text.Outcome.Provider);
        Assert.Equal("image", image.Content[0].Type);
        Assert.Equal("text", failed.Content[0].Type);
        Assert.Equal("Error: boom", failed.Content[0].Text);
        Assert.Equal("failed", failed.Outcome!.Status);
    }

    [Fact]
    public async Task ACallCancelledBeforeItStartsNeverRuns()
    {
        var tool = new Immediate("fast");
        var registry = new ToolRegistry();
        registry.RegisterTool(tool);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            registry.ExecuteToolAsync("fast", null, new CancellationToken(canceled: true))
        );

        Assert.Equal(0, tool.Calls);
    }

    [Fact]
    public async Task AToolWhoseDefinitionFailsIsAnError()
    {
        var registry = new ToolRegistry();
        registry.RegisterTool(new BrokenDefinition());

        var result = await registry.ExecuteToolAsync("broken", null);

        Assert.True(result.IsError);
        Assert.Equal("text", result.Content[0].Type);
        Assert.Equal("Error: no annotations", result.Content[0].Text);
    }

    [Fact]
    public async Task ANestedCallInACancelledOperationDoesNotRun()
    {
        var child = new Immediate("child");
        var registry = new ToolRegistry();
        registry.RegisterTool(child);
        registry.RegisterTool(
            new Delegating(
                "parent",
                () =>
                {
                    OperationContext.Current.Value!.Stop.Cancel();
                    return registry.ExecuteToolAsync("child", null).GetAwaiter().GetResult();
                }
            )
        );

        var result = await registry.ExecuteToolAsync("parent", null);

        Assert.True(result.IsError);
        Assert.Equal(0, child.Calls);
    }

    [Fact]
    public async Task ATargetResolvedForACancelledOperationIsNotActedOn()
    {
        var tool = new Immediate("fast");
        var registry = new ToolRegistry
        {
            ResolveTarget = _ =>
            {
                OperationContext.Current.Value!.Stop.Cancel();
                return new ProcessIdentity(123, 456);
            },
        };
        registry.RegisterTool(tool);

        var result = await registry.ExecuteToolAsync("fast", null);

        Assert.True(result.IsError);
        Assert.Equal(0, tool.Calls);
    }

    [Fact]
    public async Task TheResolvedTargetIsRecordedOnTheOperation()
    {
        var registry = new ToolRegistry { ResolveTarget = _ => new ProcessIdentity(123, 456) };
        registry.RegisterTool(new Immediate("fast"));

        var result = await registry.ExecuteToolAsync("fast", null);

        var status = JsonSerializer
            .SerializeToElement(registry.Operations.Status(result.Outcome!.OperationId))
            .EnumerateArray()
            .Single();
        Assert.Equal(123, status.GetProperty("ProcessId").GetInt32());
        Assert.Equal(456, status.GetProperty("ProcessStartedTicks").GetInt64());
    }

    [Theory]
    [InlineData(new[] { "first", "second" }, "first; second")]
    [InlineData(new[] { " " }, "Tool reported a failure.")]
    public async Task AFailedOperationKeepsTheToolsWords(string[] texts, string error)
    {
        var registry = new ToolRegistry();
        registry.RegisterTool(
            new Delegating(
                "fails",
                () =>
                    new McpToolResult
                    {
                        Content = texts.Select(t => new McpContent { Type = "text", Text = t }).ToList(),
                        IsError = true,
                    }
            )
        );

        var result = await registry.ExecuteToolAsync("fails", null);

        var status = JsonSerializer
            .SerializeToElement(registry.Operations.Status(result.Outcome!.OperationId))
            .EnumerateArray()
            .Single();
        Assert.Equal(error, status.GetProperty("Error").GetString());
    }

    [Fact]
    public async Task AToolsOwnOperationIdIsKept()
    {
        var registry = new ToolRegistry();
        registry.RegisterTool(
            new Delegating(
                "own-id",
                () =>
                    new McpToolResult
                    {
                        Content = [new() { Type = "text", Text = "x" }],
                        Outcome = new ToolOutcome("completed", "returned", OperationId: "mine"),
                    }
            )
        );

        var result = await registry.ExecuteToolAsync("own-id", null);

        Assert.Equal("mine", result.Outcome!.OperationId);
    }

    [Fact]
    public void ANonPositiveTimeoutIsExplained()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => new ToolRegistry(TimeSpan.Zero));

        Assert.StartsWith("Tool timeout must be greater than zero.", error.Message);
    }

    [Fact]
    public void ArgumentsMissingFromTheCallUseTheirDefaults()
    {
        var probe = new Immediate("probe");

        Assert.Equal(0, probe.Number(Json(new { other = 1 })));
        Assert.True(probe.Flag(Json(new { other = 1 }), true));
    }

    /// <summary>Hands out the next line only once every earlier request has its response, so none overlap.</summary>
    private sealed class PacedReader(IEnumerable<string> lines, StringWriter output) : TextReader
    {
        private readonly Queue<string> pending = new(lines);
        private int handedOut;

        public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            var answered = handedOut;
            await Eventually.WaitUntilAsync(
                () => output.ToString().Count(c => c == '\n') >= answered,
                $"response {answered}"
            );
            handedOut++;
            return pending.Count > 0 ? pending.Dequeue() : null;
        }
    }

    private sealed class Immediate(string name) : ToolBase
    {
        public int Calls { get; private set; }
        public override string Name => name;
        public override string Description => "no desktop";
        public override object InputSchema => new { };

        public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
        {
            Calls++;
            return Task.FromResult(TextResult("done"));
        }

        public int Number(JsonElement arguments) => GetArgument<int>(arguments, "number");

        public bool Flag(JsonElement arguments, bool fallback) => GetBoolArgument(arguments, "flag", fallback);
    }

    private sealed class Picture : ToolBase
    {
        public override string Name => "picture";
        public override string Description => "no desktop";
        public override object InputSchema => new { };

        public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments) =>
            Task.FromResult(ImageResult([1, 2, 3]));
    }

    private sealed class Throws(Exception error) : ToolBase
    {
        public override string Name => "throws";
        public override string Description => "no desktop";
        public override object InputSchema => new { };

        public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments) => throw error;
    }

    private sealed class Gated(Task gate) : ToolBase
    {
        public override string Name => "gated";
        public override string Description => "no desktop";
        public override object InputSchema => new { };

        public override async Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
        {
            await gate;
            return TextResult("released");
        }
    }

    private sealed class BrokenDefinition : ToolBase
    {
        public override string Name => "broken";
        public override string Description => "no desktop";
        public override object InputSchema => new { };
        public override McpToolAnnotations? Annotations => throw new InvalidOperationException("no annotations");

        public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments) => Task.FromResult(TextResult("x"));
    }

    private sealed class Delegating(string name, Func<McpToolResult> run) : ToolBase
    {
        public override string Name => name;
        public override string Description => "no desktop";
        public override object InputSchema => new { };

        public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments) => Task.FromResult(run());
    }
}
