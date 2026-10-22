using System.Text.Json;
using FlaUI.Mcp;
using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// A stranded batch must mark its app's provider pending, as a stranded single tool does, so later ref tools
/// fail fast instead of hanging until their own timeout. That needs the batch's target.
/// </summary>
public sealed class BatchTargetTests
{
    private static JsonElement Args(object value) => JsonSerializer.SerializeToElement(value);

    [Fact]
    public void ABatchResolvesToTheTargetOfItsFirstTargetedStep()
    {
        using var sessions = new SessionManager(null, ProcessPolicy.AllowAll);
        var handle = sessions.PublishWindow(null, 42, 7, new(42, 7, 9));

        var target = sessions.ResolveTarget(
            Args(
                new
                {
                    actions = new object[]
                    {
                        new { action = "wait", ms = 1 },
                        new
                        {
                            action = "type",
                            handle,
                            text = "x",
                        },
                    },
                }
            ),
            new ElementRegistry()
        );

        Assert.Equal(new ProcessIdentity(7, 9), target);
    }

    [Fact]
    public void ABatchWithNoTargetedStepResolvesToNothing()
    {
        using var sessions = new SessionManager(null, ProcessPolicy.AllowAll);

        Assert.Null(
            sessions.ResolveTarget(
                Args(new { actions = new object[] { new { action = "wait" } } }),
                new ElementRegistry()
            )
        );
    }

    [Fact]
    public async Task AStrandedBatchBlocksItsAppForLaterRefTools()
    {
        using var installed = new FakeDesktop().Install();
        using var sessions = new SessionManager(null, ProcessPolicy.AllowAll);
        var handle = sessions.PublishWindow(null, 42, 7, new(42, 7, 9));
        var pending = new PendingInvokeTracker(_ => 9);
        var elements = new ElementRegistry();
        var clock = new ManualTimeProvider();
        var registry = new ToolRegistry(TimeSpan.FromMilliseconds(100), time: clock)
        {
            ResolveTarget = arguments => sessions.ResolveTarget(arguments, elements),
            Pending = pending,
        };
        var gate = new TaskCompletionSource<McpToolResult>();
        var step = new HangingStep(gate.Task);
        registry.RegisterTool(step);
        registry.RegisterTool(new BatchTool(registry));
        try
        {
            var call = registry.ExecuteToolAsync(
                "windows_batch",
                Args(
                    new
                    {
                        actions = new object[]
                        {
                            new
                            {
                                action = "type",
                                handle,
                                text = "x",
                            },
                        },
                    }
                )
            );
            await step.Entered.Task.WaitAsync(Eventually.Bound);
            clock.Advance(TimeSpan.FromMilliseconds(100));
            var result = await call.WaitAsync(Eventually.Bound);

            Assert.Equal("timed_out_pending", result.Outcome!.Dispatch);
            Assert.True(pending.TryGetPending(7, out var blocked));
            Assert.Equal("timed-out windows_batch", blocked.Description);
        }
        finally
        {
            gate.SetResult(new McpToolResult { Content = [] });
        }
    }

    private sealed class HangingStep(Task<McpToolResult> gate) : ToolBase
    {
        public override string Name => "windows_type";
        public override string Description => "no desktop";
        public override object InputSchema => new { };

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
        {
            Entered.TrySetResult();
            return gate;
        }
    }
}
