using System.Text.Json;
using FlaUI.Mcp;
using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>Tests written to kill mutants that survived in how the registry strands timed-out and cancelled calls.</summary>
public sealed class StrandedCallMutationTests
{
    [Fact]
    public async Task ATimedOutCallSaysWhatMayStillBeRunningAndBlocksItsApp()
    {
        var gate = new TaskCompletionSource();
        var pending = new PendingInvokeTracker(_ => 456);
        var clock = new ManualTimeProvider();
        var registry = new ToolRegistry(TimeSpan.FromMilliseconds(100), time: clock)
        {
            ResolveTarget = _ => new ProcessIdentity(123, 456),
            Pending = pending,
        };
        var gated = new Gated(gate.Task);
        registry.RegisterTool(gated);
        try
        {
            var call = registry.ExecuteToolAsync("gated", null);
            await gated.Entered.Task.WaitAsync(Eventually.Bound);
            clock.Advance(TimeSpan.FromMilliseconds(100));
            var result = await call.WaitAsync(Eventually.Bound);

            Assert.Equal("text", result.Content[0].Type);
            Assert.Equal(
                "Tool 'gated' timed out after 100ms. A modal dialog or blocked UI Automation provider may still be running in the background. "
                    + "Inspect the target and operation status before continuing. The request may have partially executed; do not blindly replay it.",
                result.Content[0].Text
            );
            Assert.Equal("unknown", result.Outcome!.Provider);
            Assert.True(pending.TryGetPending(123, out var blocked));
            Assert.Equal("timed-out gated", blocked.Description);
        }
        finally
        {
            gate.SetResult();
        }
    }

    [Fact]
    public async Task ACancelledCallSaysCancellationWasRequested()
    {
        var gate = new TaskCompletionSource();
        var registry = new ToolRegistry(TimeSpan.FromSeconds(10), time: new ManualTimeProvider());
        registry.RegisterTool(new Gated(gate.Task));
        using var cancel = new CancellationTokenSource();
        try
        {
            var call = registry.ExecuteToolAsync("gated", null, cancel.Token);
            await cancel.CancelAsync();
            var result = await call.WaitAsync(Eventually.Bound);

            Assert.StartsWith("Tool 'gated' cancellation requested. A modal dialog", result.Content[0].Text);
        }
        finally
        {
            gate.SetResult();
        }
    }

    [Fact]
    public async Task ACancelledCallKeepsTheSlotOnlyUntilItsTimeoutWouldHaveExpired()
    {
        var gate = new TaskCompletionSource();
        var clock = new ManualTimeProvider();
        var registry = new ToolRegistry(TimeSpan.FromMilliseconds(600), time: clock);
        registry.RegisterTool(new Gated(gate.Task));
        Task? settled = null;
        using var cancel = new CancellationTokenSource();
        try
        {
            var call = registry.ExecuteToolAsync("gated", null, cancel.Token, whenStranded: task => settled = task);
            clock.Advance(TimeSpan.FromMilliseconds(400));
            await cancel.CancelAsync();
            await call.WaitAsync(Eventually.Bound);

            // Due at 600 ms from the start, so 200 ms after the cancel: not sooner (the 400 ms counted twice)
            // and not later (the 400 ms not counted at all).
            clock.Advance(TimeSpan.FromMilliseconds(199));
            Assert.False(settled!.IsCompleted, "The slot was freed before the call's timeout would have expired.");
            clock.Advance(TimeSpan.FromMilliseconds(1));
            await settled.WaitAsync(Eventually.Bound);
        }
        finally
        {
            gate.SetResult();
        }
    }

    [Fact]
    public async Task ATimedOutCallReportsTheInputItAlreadySent()
    {
        var gate = new TaskCompletionSource();
        var clock = new ManualTimeProvider();
        var registry = new ToolRegistry(TimeSpan.FromMilliseconds(100), time: clock);
        var sends = new SendsThenWaits(gate.Task);
        registry.RegisterTool(sends);
        try
        {
            var call = registry.ExecuteToolAsync("sends-then-waits", null);
            await sends.Sent.Task.WaitAsync(Eventually.Bound);
            clock.Advance(TimeSpan.FromMilliseconds(100));
            var result = await call.WaitAsync(Eventually.Bound);

            Assert.Equal("timed_out_pending", result.Outcome!.Dispatch);
            Assert.Equal(1, result.Outcome.InputsSent);
        }
        finally
        {
            gate.SetResult();
        }
    }

    [Fact]
    public async Task ToolErrorsAreText()
    {
        var result = await new ErrorProbe().ExecuteAsync(null);

        Assert.True(result.IsError);
        Assert.Equal("text", result.Content[0].Type);
    }

    private sealed class SendsThenWaits(Task gate) : ToolBase
    {
        public TaskCompletionSource Sent { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override string Name => "sends-then-waits";
        public override string Description => "no desktop";
        public override object InputSchema => new { };

        public override async Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
        {
            MutationGuard.RecordDispatch();
            Sent.TrySetResult();
            await gate;
            return TextResult("released");
        }
    }

    private sealed class ErrorProbe : ToolBase
    {
        public override string Name => "error-probe";
        public override string Description => "no desktop";
        public override object InputSchema => new { };

        public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments) =>
            Task.FromResult(ErrorResult("refused"));
    }

    private sealed class Gated(Task gate) : ToolBase
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override string Name => "gated";
        public override string Description => "no desktop";
        public override object InputSchema => new { };

        public override async Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
        {
            Entered.TrySetResult();
            await gate;
            return TextResult("released");
        }
    }
}
