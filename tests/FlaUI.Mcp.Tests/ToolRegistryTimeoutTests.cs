using System.Text.Json;
using FlaUI.Mcp;
using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Tests;

public class ToolRegistryTimeoutTests
{
    [Fact]
    public async Task TargetMetadataResolutionIsInsideTimeout()
    {
        using var release = new ManualResetEventSlim();
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clock = new ManualTimeProvider();
        var registry = new ToolRegistry(TimeSpan.FromMilliseconds(50), time: clock)
        {
            ResolveTarget = _ =>
            {
                try
                {
                    release.Wait(Eventually.Bound);
                    return null;
                }
                finally
                {
                    finished.TrySetResult();
                }
            },
        };
        registry.RegisterTool(new SuccessfulTool());
        try
        {
            var call = registry.ExecuteToolAsync("successful", null);
            clock.Advance(TimeSpan.FromMilliseconds(50));
            var result = await call.WaitAsync(Eventually.Bound);
            Assert.True(result.IsError);
            Assert.Contains("timed out", result.Content[0].Text);
        }
        finally
        {
            release.Set();
            await finished.Task.WaitAsync(Eventually.Bound);
        }
    }

    [Fact]
    public async Task ExecuteToolAsync_TimesOutSynchronousToolBody()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clock = new ManualTimeProvider();
        var registry = new ToolRegistry(TimeSpan.FromSeconds(30), time: clock);
        registry.RegisterTool(new BlockingTool(entered, release, finished));

        // The clock moves only once the tool body runs, so the timeout cannot strand the call before it starts
        // (which a 50 ms wall-clock timeout did on a starved thread pool). The waits only bound a hung test.
        var allowance = TimeSpan.FromSeconds(10);
        try
        {
            var execution = registry.ExecuteToolAsync("blocking", arguments: null);
            Assert.True(entered.Wait(allowance), "The synchronous tool body did not start.");
            Assert.False(execution.IsCompleted);
            clock.Advance(TimeSpan.FromSeconds(29));
            Assert.False(execution.IsCompleted);
            clock.Advance(TimeSpan.FromSeconds(1));
            var result = await execution.WaitAsync(allowance);

            Assert.True(result.IsError);
            Assert.Contains("timed out", result.Content[0].Text);
        }
        finally
        {
            release.Set();
            if (entered.IsSet)
            {
                await finished.Task.WaitAsync(allowance);
            }
        }
    }

    [Fact]
    public async Task ExecuteToolAsync_ReturnsSuccessfulToolResult()
    {
        var registry = new ToolRegistry(TimeSpan.FromSeconds(1), time: new ManualTimeProvider());
        registry.RegisterTool(new SuccessfulTool());

        var result = await registry.ExecuteToolAsync("successful", arguments: null);

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("ok", result.Content[0].Text);
    }

    [Fact]
    public async Task ExecuteToolAsync_InvokesActivityCallbackForKnownToolsOnly()
    {
        var activity = 0;
        var registry = new ToolRegistry(TimeSpan.FromSeconds(1), onToolActivity: () => activity++);
        registry.RegisterTool(new SuccessfulTool());

        await registry.ExecuteToolAsync("successful", arguments: null);
        await registry.ExecuteToolAsync("unknown", arguments: null);

        Assert.Equal(1, activity);
    }

    [Fact]
    public async Task FailedPowerRequestDoesNotBlockToolExecution()
    {
        using var request = new PowerAvailabilityRequest(() => false, () => true, () => true, () => true, () => { });
        using var keepAwake = new KeepAwake(TimeSpan.FromMinutes(5), request.Set, request.Clear);
        var registry = new ToolRegistry(onToolActivity: keepAwake.Poke);
        registry.RegisterTool(new SuccessfulTool());

        var result = await registry.ExecuteToolAsync("successful", arguments: null);

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("ok", result.Content[0].Text);
        Assert.False(keepAwake.IsActive);
    }

    private sealed class BlockingTool : ITool
    {
        private readonly ManualResetEventSlim _entered;
        private readonly ManualResetEventSlim _release;
        private readonly TaskCompletionSource _finished;

        public BlockingTool(ManualResetEventSlim entered, ManualResetEventSlim release, TaskCompletionSource finished)
        {
            _entered = entered;
            _release = release;
            _finished = finished;
        }

        public string Name => "blocking";

        public McpTool GetDefinition() =>
            new()
            {
                Name = Name,
                Description = "Blocks synchronously",
                InputSchema = new { type = "object" },
            };

        public Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
        {
            _entered.Set();
            try
            {
                _release.Wait();
                return Task.FromResult(
                    new McpToolResult
                    {
                        Content = new List<McpContent>
                        {
                            new() { Type = "text", Text = "late" },
                        },
                    }
                );
            }
            finally
            {
                _finished.TrySetResult();
            }
        }
    }

    private sealed class SuccessfulTool : ITool
    {
        public string Name => "successful";

        public McpTool GetDefinition() =>
            new()
            {
                Name = Name,
                Description = "Succeeds",
                InputSchema = new { type = "object" },
            };

        public Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
        {
            return Task.FromResult(
                new McpToolResult
                {
                    Content = new List<McpContent>
                    {
                        new() { Type = "text", Text = "ok" },
                    },
                }
            );
        }
    }
}
