using System.Text.Json;
using FlaUI.Mcp;
using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// A tool that fails after some of its input went out must say so: a broker that reads
/// "not dispatched" may replay input that already reached the app.
/// </summary>
public sealed class DispatchOutcomeTests
{
    private static JsonElement Args(object value) => JsonSerializer.SerializeToElement(value);

    private static JsonElement Outcome(McpToolResult result) => JsonSerializer.SerializeToElement(result.Outcome);

    private static void AssertDispatch(McpToolResult result, string dispatch, int inputsSent)
    {
        var outcome = Outcome(result);
        Assert.Equal(dispatch, outcome.GetProperty("dispatch").GetString());
        Assert.Equal(inputsSent, outcome.GetProperty("inputsSent").GetInt32());
    }

    private static (SessionManager Sessions, string Handle) Window()
    {
        var sessions = new SessionManager(null, ProcessPolicy.AllowAll);
        var handle = sessions.PublishWindow(null, FakeDesktop.TargetHwnd, FakeDesktop.TargetPid, FakeDesktop.Target);
        return (sessions, handle);
    }

    [Fact]
    public async Task SendKeysThatLosesFocusAfterTwoOfThreeChordsReportsPartialDispatch()
    {
        var desktop = new FakeDesktop { FocusLostAfter = 2 };
        using var installed = desktop.Install();
        var (sessions, handle) = Window();
        using var _ = sessions;
        var registry = new ToolRegistry();
        registry.RegisterTool(new SendKeysTool(new ElementRegistry(), sessions: sessions));

        var result = await registry.ExecuteToolAsync(
            "windows_send_keys",
            Args(new { handle, keys = new[] { "Tab", "Tab", "Enter" } })
        );

        Assert.True(result.IsError);
        Assert.Contains("Completed 2 chord(s)", result.Content[0].Text);
        Assert.Equal(2, desktop.Dispatched);
        AssertDispatch(result, "partial", 2);
        Assert.Equal("failed", Outcome(result).GetProperty("status").GetString());
    }

    [Fact]
    public async Task FillKeyboardFallbackThatFailsVerificationReportsPartialDispatch()
    {
        // Select-all and one typed chunk go out; the observation then sees focus has moved.
        var desktop = new FakeDesktop { FocusLostAfter = 2 };
        using var installed = desktop.Install();
        var registry = new ToolRegistry();
        registry.RegisterTool(
            new DelegateTool(
                "fill-fallback",
                () =>
                {
                    using var input = new GuardedInput(FakeDesktop.Target);
                    return FillTool.FillByKeyboard(input, "replacement", "field", () => new("", "Value"), () => { });
                }
            )
        );

        var result = await registry.ExecuteToolAsync("fill-fallback", null);

        Assert.True(result.IsError);
        Assert.Contains("changed", result.Content[0].Text);
        AssertDispatch(result, "partial", 2);
    }

    [Fact]
    public async Task ClickPatternThenRefusedFallbackReportsTheDispatchedPattern()
    {
        // ClickTool's order: Select through the modal-aware invoker, then a physical fallback click
        // that Windows refuses to activate.
        var desktop = new FakeDesktop { ForegroundWindow = FakeDesktop.OtherHwnd, ActivationAllowed = false };
        using var installed = desktop.Install();
        var registry = new ToolRegistry();
        registry.RegisterTool(
            new DelegateTool(
                "click-fallback",
                () =>
                {
                    using (GuardedInput.AcquireLease())
                    {
                        ModalAwareInvoker.Execute(
                            0,
                            "Select on 'row'",
                            () => MutationGuard.Execute(() => { }, () => { }),
                            new PendingInvokeTracker(),
                            TimeSpan.FromSeconds(5),
                            _ => []
                        );
                    }

                    using var input = new GuardedInput(FakeDesktop.Target);
                    return new McpToolResult { Content = [new() { Type = "text", Text = "clicked" }] };
                }
            )
        );

        var result = await registry.ExecuteToolAsync("click-fallback", null);

        Assert.True(result.IsError);
        Assert.Contains("activation-denied", result.Content[0].Text);
        Assert.Equal(0, desktop.Dispatched);
        AssertDispatch(result, "partial", 1);
    }

    [Fact]
    public async Task WindowStateThatDoesNotSettleReportsTheSentRequest()
    {
        var desktop = new FakeDesktop { State = "normal" };
        using var installed = desktop.Install();
        var (sessions, handle) = Window();
        using var _ = sessions;
        var registry = new ToolRegistry();
        registry.RegisterTool(new WindowStateTool(new ElementRegistry(), new PendingInvokeTracker(), sessions));

        var result = await registry.ExecuteToolAsync(
            "windows_set_window_state",
            Args(new { handle, state = "maximize" })
        );

        Assert.True(result.IsError);
        Assert.Contains("did not settle", result.Content[0].Text);
        Assert.Equal([WindowStates.Maximized], desktop.StateRequests);
        AssertDispatch(result, "completed", 1);
        Assert.Equal("not-settled", Outcome(result).GetProperty("verification").GetString());
    }

    [Fact]
    public async Task RefusedToolReportsNothingDispatched()
    {
        var registry = new ToolRegistry();
        registry.RegisterTool(new DelegateTool("refuses", () => throw new InvalidOperationException("no input sent")));

        var result = await registry.ExecuteToolAsync("refuses", null);

        Assert.True(result.IsError);
        AssertDispatch(result, "not-dispatched", 0);
        Assert.Equal("failed", Outcome(result).GetProperty("status").GetString());
        Assert.False(string.IsNullOrEmpty(result.Outcome!.OperationId));
    }

    [Fact]
    public async Task NestedCallsCountOnlyTheirOwnInputs()
    {
        var registry = new ToolRegistry();
        registry.RegisterTool(new DelegateTool("one-input", () => Dispatch(1, fail: false)));
        registry.RegisterTool(new DelegateTool("two-then-fail", () => Dispatch(2, fail: true)));
        registry.RegisterTool(new BatchTool(registry));
        registry.RegisterTool(
            new DelegateTool(
                "nested",
                () =>
                {
                    var first = registry.ExecuteToolAsync("one-input", null).GetAwaiter().GetResult();
                    var second = registry.ExecuteToolAsync("two-then-fail", null).GetAwaiter().GetResult();
                    AssertDispatch(first, "completed", 1);
                    AssertDispatch(second, "partial", 2);
                    return first;
                }
            )
        );

        var result = await registry.ExecuteToolAsync("nested", null);

        AssertDispatch(result, "completed", 3);
    }

    [Fact]
    public async Task BatchThatFailsAfterCompletedStepsReportsPartialAndFailedStatus()
    {
        var registry = new ToolRegistry();
        registry.RegisterTool(new DelegateTool("windows_click", () => Dispatch(1, fail: false)));
        registry.RegisterTool(new DelegateTool("windows_type", () => Dispatch(0, fail: true)));
        registry.RegisterTool(new BatchTool(registry));

        var result = await registry.ExecuteToolAsync(
            "windows_batch",
            Args(
                new
                {
                    actions = new object[]
                    {
                        new { action = "click", @ref = "w1e1" },
                        new { action = "type", text = "x" },
                    },
                }
            )
        );

        Assert.NotEqual(true, result.IsError); // The batch keeps its legacy numbered-text contract.
        AssertDispatch(result, "partial", 1);
        Assert.Equal("failed", Outcome(result).GetProperty("status").GetString());
        var status = JsonSerializer.SerializeToElement(registry.Operations.Status())[0];
        Assert.Equal("failed", status.GetProperty("status").GetString());
        Assert.Equal("partial", status.GetProperty("dispatch").GetString());
    }

    [Theory]
    [InlineData(0, false, "ok", "completed")]
    [InlineData(0, true, "failed", "not-dispatched")]
    [InlineData(1, true, "failed", "partial")]
    public async Task OperationStatusSeparatesToolFailureFromDispatch(
        int inputs,
        bool fail,
        string expectedStatus,
        string expectedDispatch
    )
    {
        var registry = new ToolRegistry();
        registry.RegisterTool(new DelegateTool("tool", () => Dispatch(inputs, fail)));

        await registry.ExecuteToolAsync("tool", null);

        var status = JsonSerializer.SerializeToElement(registry.Operations.Status())[0];
        Assert.Equal(expectedStatus, status.GetProperty("status").GetString());
        Assert.Equal(expectedDispatch, status.GetProperty("dispatch").GetString());
        Assert.Equal(inputs, status.GetProperty("inputsSent").GetInt32());
    }

    /// <summary>Records <paramref name="inputs"/> mutations the way tools do, then succeeds or fails.</summary>
    private static McpToolResult Dispatch(int inputs, bool fail)
    {
        for (var i = 0; i < inputs; i++)
        {
            MutationGuard.Execute(() => { }, () => { });
        }

        return new McpToolResult
        {
            Content = [new() { Type = "text", Text = fail ? "failed" : "done" }],
            IsError = fail ? true : null,
        };
    }

    private sealed class DelegateTool(string name, Func<McpToolResult> run) : ToolBase
    {
        public override string Name => name;
        public override string Description => "no desktop";
        public override object InputSchema => new { };

        public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments) => Task.FromResult(run());
    }
}
