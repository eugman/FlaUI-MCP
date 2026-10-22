using System.Text.Json;
using FlaUI.Mcp;
using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// A client that reads only isError must see a failed call as failed: a batch whose step failed after input
/// went out, and a call stranded by its timeout. A stranded call keeps the refusal it already recorded.
/// </summary>
public sealed class FailedResultStatusTests
{
    private static JsonElement[] Rows(string json) => JsonSerializer.Deserialize<JsonElement[]>(json)!;

    [Fact]
    public async Task ABatchWhoseStepFailsAfterInputIsAnError()
    {
        var calls = 0;
        var result = await BatchTool.ExecuteRows(
            Rows("""[{"action":"click","ref":"first"},{"action":"type","text":"x"}]"""),
            (_, _) =>
                Task.FromResult(
                    ++calls == 1
                        ? new McpToolResult
                        {
                            Content = [new() { Type = "text", Text = "Clicked first" }],
                            Outcome = new("completed", "returned") { InputsSent = 1, Status = "ok" },
                        }
                        : new McpToolResult
                        {
                            IsError = true,
                            Content = [new() { Type = "text", Text = "focus moved" }],
                            Outcome = new("not-dispatched", "not-tracked") { InputsSent = 0, Status = "failed" },
                        }
                )
        );

        Assert.True(result.IsError);
        Assert.Equal("partial", result.Outcome!.Dispatch);
        Assert.Equal("failed", result.Outcome.Status);
        Assert.Equal(1, result.Outcome.InputsSent);
    }

    [Fact]
    public async Task ABatchWithNoFailedStepIsNotAnError()
    {
        var result = await BatchTool.ExecuteRows(
            Rows("""[{"action":"click","ref":"first"}]"""),
            (_, _) => Task.FromResult(new McpToolResult { Content = [new() { Type = "text", Text = "Clicked" }] })
        );

        Assert.NotEqual(true, result.IsError);
        Assert.Null(result.Outcome!.Status);
    }

    [Fact]
    public async Task AStrandedCallIsFailedAndKeepsTheRefusalItRecorded()
    {
        var gate = new TaskCompletionSource();
        var clock = new ManualTimeProvider();
        var registry = new ToolRegistry(TimeSpan.FromMilliseconds(100), time: clock);
        var refusal = new InputRefusal("activation-denied", "activation-denied: kept in front. No input sent.");
        var refused = new RefusesThenHangs(refusal, gate.Task);
        registry.RegisterTool(refused);
        try
        {
            var call = registry.ExecuteToolAsync("refuses-then-hangs", null);
            await refused.Entered.Task.WaitAsync(Eventually.Bound);
            clock.Advance(TimeSpan.FromMilliseconds(100));
            var result = await call.WaitAsync(Eventually.Bound);

            Assert.True(result.IsError);
            Assert.Equal("timed_out_pending", result.Outcome!.Dispatch);
            Assert.Equal("failed", result.Outcome.Status);
            Assert.Equal("activation-denied", result.Outcome.Failure!.Code);
            Assert.Equal(1, result.Outcome.InputsSent);
            Assert.True(result.Outcome.Failure.ActionDispatched);
        }
        finally
        {
            gate.SetResult();
        }
    }

    private sealed class RefusesThenHangs(InputRefusal refusal, Task gate) : ToolBase
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override string Name => "refuses-then-hangs";
        public override string Description => "no desktop";
        public override object InputSchema => new { };

        public override async Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
        {
            MutationGuard.RecordDispatch();
            _ = InputRefusedException.Record(refusal);
            Entered.TrySetResult();
            await gate;
            return TextResult("late");
        }
    }
}
