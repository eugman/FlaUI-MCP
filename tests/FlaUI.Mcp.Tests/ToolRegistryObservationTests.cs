using System.Text.Json;
using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Tests;

public sealed class ToolRegistryObservationTests
{
    [Fact]
    public async Task SettledSuccessfulReadInvokesObservationHook()
    {
        var registry = new ToolRegistry();
        var observed = 0;
        registry.RegisterTool(new ReadProbeTool(() => Task.FromResult(new McpToolResult())));
        registry.ObservedAfterSuccess = (name, _, outcome, _) =>
        {
            Assert.Equal("read_probe", name);
            Assert.Equal("ok", outcome.Status);
            observed++;
        };

        var result = await registry.ExecuteToolAsync("read_probe", null);

        Assert.NotEqual(true, result.IsError);
        Assert.Equal(1, observed);
    }

    [Fact]
    public async Task FailedAndPendingReadsDoNotInvokeObservationHook()
    {
        var gate = new TaskCompletionSource<McpToolResult>();
        var clock = new ManualTimeProvider();
        var registry = new ToolRegistry(TimeSpan.FromMilliseconds(100), time: clock);
        var observed = 0;
        registry.ObservedAfterSuccess = (_, _, _, _) => observed++;
        registry.RegisterTool(new ReadProbeTool(() => Task.FromResult(new McpToolResult { IsError = true })));
        Assert.True((await registry.ExecuteToolAsync("read_probe", null)).IsError);
        registry.RegisterTool(new ReadProbeTool(() => gate.Task));

        var call = registry.ExecuteToolAsync("read_probe", null);
        clock.Advance(TimeSpan.FromMilliseconds(100));
        var pending = await call.WaitAsync(Eventually.Bound);
        Assert.True(pending.IsError);
        gate.SetResult(new McpToolResult());
        Assert.Equal(0, observed);
    }

    [Fact]
    public async Task ExecutionScopeStaysHeldUntilStrandedToolSettles()
    {
        var gate = new TaskCompletionSource<McpToolResult>();
        var entered = new TaskCompletionSource();
        var released = new TaskCompletionSource();
        var clock = new ManualTimeProvider();
        var registry = new ToolRegistry(TimeSpan.FromMilliseconds(100), time: clock);
        registry.RegisterTool(
            new ReadProbeTool(() =>
            {
                entered.SetResult();
                return gate.Task;
            })
        );
        registry.ExecutionScopeForTool = (_, _, _) =>
            ValueTask.FromResult<IAsyncDisposable?>(new ProbeScope(() => released.SetResult()));

        var call = registry.ExecuteToolAsync("read_probe", null);
        await entered.Task.WaitAsync(Eventually.Bound);
        clock.Advance(TimeSpan.FromMilliseconds(100));
        var timedOut = await call.WaitAsync(Eventually.Bound);
        Assert.True(timedOut.IsError);
        Assert.False(released.Task.IsCompleted);

        gate.SetResult(new McpToolResult());
        await released.Task.WaitAsync(Eventually.Bound);
    }

    [Fact]
    public async Task FailedInputNotificationRunsBeforeExecutionScopeReleases()
    {
        var events = new List<string>();
        var registry = new ToolRegistry
        {
            ResolveTarget = _ => new ProcessIdentity(123, 456),
            ExecutionScopeForTool = (_, _, _) =>
                ValueTask.FromResult<IAsyncDisposable?>(new ProbeScope(() => events.Add("released"))),
            FailedAfterInputWithTarget = (_, outcome, target) =>
            {
                Assert.Equal(123, target?.ProcessId);
                Assert.Equal(1, outcome.InputsSent);
                events.Add("paused");
            },
        };
        registry.RegisterTool(
            new ReadProbeTool(() =>
            {
                MutationGuard.RecordDispatch();
                return Task.FromResult(new McpToolResult { IsError = true });
            })
        );

        var result = await registry.ExecuteToolAsync("read_probe", null);

        Assert.True(result.IsError);
        Assert.Equal(["paused", "released"], events);
    }

    [Fact]
    public async Task ObservationCallbackFailureDoesNotRewriteSuccessfulRead()
    {
        var registry = new ToolRegistry
        {
            ObservedAfterSuccess = (_, _, _, _) => throw new InvalidOperationException("observer failed"),
        };
        registry.RegisterTool(new ReadProbeTool(() => Task.FromResult(new McpToolResult())));

        var result = await registry.ExecuteToolAsync("read_probe", null);

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("ok", result.Outcome?.Status);
    }

    [Fact]
    public async Task ScopeAcquisitionFailureIsSettledWithoutDispatch()
    {
        var registry = new ToolRegistry { ExecutionScopeForTool = (_, _, _) => throw new ProbeRefusal() };
        registry.RegisterTool(new ReadProbeTool(() => throw new InvalidOperationException("should not run")));

        var result = await registry.ExecuteToolAsync("read_probe", null);

        Assert.True(result.IsError);
        Assert.Equal("failed", result.Outcome?.Status);
        Assert.Equal(0, result.Outcome?.InputsSent);
        Assert.Equal("busy", result.Outcome?.Failure?.Code);
        Assert.False(result.Outcome?.Failure?.ActionDispatched);
        Assert.Contains("busy", result.Content[0].Text);
        var status = JsonSerializer.SerializeToElement(registry.Operations.Status(result.Outcome!.OperationId))[0];
        Assert.Equal("failed", status.GetProperty("status").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StrandedDispatchedCallPausesOnceEvenIfItLaterSettles(bool cancel)
    {
        var gate = new TaskCompletionSource<McpToolResult>();
        var entered = new TaskCompletionSource();
        var settled = new TaskCompletionSource();
        var clock = new ManualTimeProvider();
        var registry = new ToolRegistry(TimeSpan.FromMilliseconds(100), time: clock)
        {
            ResolveTarget = _ => new ProcessIdentity(123, 456),
            ExecutionScopeForTool = (_, _, _) =>
                ValueTask.FromResult<IAsyncDisposable?>(new ProbeScope(() => settled.SetResult())),
        };
        var paused = 0;
        registry.FailedAfterInputWithTarget = (_, outcome, target) =>
        {
            Assert.Equal(123, target?.ProcessId);
            Assert.Equal(1, outcome.InputsSent);
            paused++;
        };
        registry.RegisterTool(
            new ReadProbeTool(() =>
            {
                MutationGuard.RecordDispatch();
                entered.SetResult();
                return gate.Task;
            })
        );
        using var cancellation = new CancellationTokenSource();
        var call = registry.ExecuteToolAsync("read_probe", null, cancellation.Token);
        await entered.Task.WaitAsync(Eventually.Bound);
        if (cancel)
        {
            cancellation.Cancel();
        }
        else
        {
            clock.Advance(TimeSpan.FromMilliseconds(100));
        }

        var stranded = await call.WaitAsync(Eventually.Bound);
        Assert.True(stranded.IsError);
        Assert.Equal(1, paused);
        gate.SetResult(new McpToolResult { IsError = true });
        await settled.Task.WaitAsync(Eventually.Bound);
        Assert.Equal(1, paused);
    }

    [Fact]
    public async Task SuccessfulZeroInputScreenshotLikeToolCanObserve()
    {
        var registry = new ToolRegistry();
        var observed = 0;
        registry.ObservedAfterSuccess = (_, _, _, _) => observed++;
        registry.RegisterTool(new ScreenshotProbeTool());

        var result = await registry.ExecuteToolAsync("screenshot_probe", null);

        Assert.NotEqual(true, result.IsError);
        Assert.Equal(1, observed);
    }

    [Fact]
    public async Task DispatchAfterPendingResponseStillPausesBeforeScopeRelease()
    {
        var proceed = new TaskCompletionSource();
        var entered = new TaskCompletionSource();
        var settled = new TaskCompletionSource();
        var clock = new ManualTimeProvider();
        var events = new List<string>();
        var registry = new ToolRegistry(TimeSpan.FromMilliseconds(100), time: clock)
        {
            ResolveTarget = _ => new ProcessIdentity(123, 456),
            ExecutionScopeForTool = (_, _, _) =>
                ValueTask.FromResult<IAsyncDisposable?>(
                    new ProbeScope(() =>
                    {
                        events.Add("released");
                        settled.SetResult();
                    })
                ),
            FailedAfterInputWithTarget = (_, outcome, _) =>
            {
                Assert.Equal(1, outcome.InputsSent);
                events.Add("paused");
            },
        };
        registry.RegisterTool(
            new ReadProbeTool(async () =>
            {
                entered.SetResult();
                await proceed.Task;
                MutationGuard.RecordDispatch();
                return new McpToolResult();
            })
        );

        var call = registry.ExecuteToolAsync("read_probe", null);
        await entered.Task.WaitAsync(Eventually.Bound);
        clock.Advance(TimeSpan.FromMilliseconds(100));
        var pending = await call.WaitAsync(Eventually.Bound);
        Assert.True(pending.IsError);
        Assert.Empty(events);

        proceed.SetResult();
        await settled.Task.WaitAsync(Eventually.Bound);
        Assert.Equal(["paused", "released"], events);
    }

    [Fact]
    public async Task ThrowingLegacyFailureObserverCannotSkipTargetAwarePause()
    {
        var paused = 0;
        var registry = new ToolRegistry
        {
            ResolveTarget = _ => new ProcessIdentity(123, 456),
            FailedAfterInput = (_, _) => throw new InvalidOperationException("legacy observer failed"),
            FailedAfterInputWithTarget = (_, outcome, target) =>
            {
                Assert.Equal(123, target?.ProcessId);
                Assert.Equal(1, outcome.InputsSent);
                paused++;
            },
        };
        registry.RegisterTool(
            new ReadProbeTool(() =>
            {
                MutationGuard.RecordDispatch();
                return Task.FromResult(new McpToolResult { IsError = true });
            })
        );

        var result = await registry.ExecuteToolAsync("read_probe", null);

        Assert.True(result.IsError);
        Assert.Equal("failed", result.Outcome?.Status);
        Assert.Equal(1, paused);
    }

    private sealed class ScreenshotProbeTool : ToolBase
    {
        public override string Name => "screenshot_probe";
        public override string Description => "Screenshot probe";
        public override object InputSchema => ToolSchema.For<NoArguments>();
        public override McpToolAnnotations Annotations => McpToolAnnotations.Input(destructive: false);

        public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments) =>
            Task.FromResult(new McpToolResult());
    }

    private sealed class ProbeRefusal() : AutomationRefusal("busy", "busy");

    private sealed class ProbeScope(Action onDispose) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            onDispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ReadProbeTool(Func<Task<McpToolResult>> run) : ToolBase
    {
        public override string Name => "read_probe";
        public override string Description => "Read probe";
        public override object InputSchema => ToolSchema.For<NoArguments>();
        public override McpToolAnnotations Annotations => McpToolAnnotations.ReadOnly;

        public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments) => run();
    }
}
