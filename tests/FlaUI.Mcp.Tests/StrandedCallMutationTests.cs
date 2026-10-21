using System.Diagnostics;
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
        var registry = new ToolRegistry(TimeSpan.FromMilliseconds(100))
        {
            ResolveTarget = _ => new ProcessIdentity(123, 456),
            Pending = pending,
        };
        registry.RegisterTool(new Gated(gate.Task));
        try
        {
            var result = await registry.ExecuteToolAsync("gated", null);

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
        var registry = new ToolRegistry(TimeSpan.FromSeconds(10));
        registry.RegisterTool(new Gated(gate.Task));
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        try
        {
            var result = await registry.ExecuteToolAsync("gated", null, cancel.Token);

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
        var registry = new ToolRegistry(TimeSpan.FromMilliseconds(600));
        registry.RegisterTool(new Gated(gate.Task));
        Task? settled = null;
        var started = Stopwatch.StartNew();
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
        try
        {
            await registry.ExecuteToolAsync("gated", null, cancel.Token, whenStranded: task => settled = task);
            await settled!.WaitAsync(TimeSpan.FromSeconds(10));

            // Due at 600ms from the start; counting the 400ms already waited twice would push it past 1000ms.
            Assert.InRange(started.ElapsedMilliseconds, 500, 950);
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
        var registry = new ToolRegistry(TimeSpan.FromMilliseconds(100));
        registry.RegisterTool(new SendsThenWaits(gate.Task));
        try
        {
            var result = await registry.ExecuteToolAsync("sends-then-waits", null);

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
        public override string Name => "sends-then-waits";
        public override string Description => "no desktop";
        public override object InputSchema => new { };

        public override async Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
        {
            MutationGuard.RecordDispatch();
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
        public override string Name => "gated";
        public override string Description => "no desktop";
        public override object InputSchema => new { };

        public override async Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
        {
            await gate;
            return TextResult("released");
        }
    }
}
