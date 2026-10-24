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
[Collection(InputLeaseCollection.Name)]
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
        Assert.Contains("lost the foreground while the fill was being read back", result.Content[0].Text);
        AssertDispatch(result, "partial", 2);
    }

    [Fact]
    public async Task ClickPatternThenRefusedFallbackReportsTheDispatchedPattern()
    {
        // ClickTool's order: Select through the modal-aware invoker, then a physical fallback click
        // that Windows refuses to activate.
        var desktop = new FakeDesktop
        {
            ForegroundWindow = FakeDesktop.OtherHwnd,
            ActivationAllowed = false,
            Clock = new ManualTimeProvider(),
        };
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
        // The fallback sent nothing, but the call as a whole did: the Select went out first.
        var failure = Outcome(result).GetProperty("failure");
        Assert.Equal("activation-denied", failure.GetProperty("code").GetString());
        Assert.True(failure.GetProperty("actionDispatched").GetBoolean());
    }

    [Fact]
    public async Task RefusedActivationIsAStructuredFailureOnTheOutcome()
    {
        var desktop = new FakeDesktop { ForegroundWindow = FakeDesktop.OtherHwnd, ActivationAllowed = false };
        using var installed = desktop.Install();
        var (sessions, handle) = Window();
        using var _ = sessions;
        var registry = new ToolRegistry();
        registry.RegisterTool(new SendKeysTool(new ElementRegistry(), sessions: sessions));

        var result = await registry.ExecuteToolAsync("windows_send_keys", Args(new { handle, chord = "Enter" }));

        Assert.True(result.IsError);
        Assert.Contains("activation-denied: Windows kept", result.Content[0].Text);
        AssertDispatch(result, "not-dispatched", 0);
        var failure = Outcome(result).GetProperty("failure");
        Assert.Equal("activation-denied", failure.GetProperty("code").GetString());
        Assert.Equal(FakeDesktop.OtherPid, failure.GetProperty("foregroundProcess").GetProperty("pid").GetInt32());
        Assert.Equal(FakeDesktop.TargetPid, failure.GetProperty("target").GetProperty("pid").GetInt32());
        Assert.False(failure.GetProperty("actionDispatched").GetBoolean());
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
        Assert.Equal(RefusalCodes.OperationFailed, result.Outcome.Failure?.Code);
        Assert.False(result.Outcome.Failure?.ActionDispatched);
    }

    [Fact]
    public async Task ASettledReturnedErrorBeforeInputHasATypedNoInputFailure()
    {
        var registry = new ToolRegistry();
        registry.RegisterTool(
            new DelegateTool(
                "fails",
                () =>
                    new McpToolResult
                    {
                        IsError = true,
                        Content = [new() { Type = "text", Text = "No window matched" }],
                    }
            )
        );

        var result = await registry.ExecuteToolAsync("fails", null);

        Assert.Equal("not-dispatched", result.Outcome?.Dispatch);
        Assert.Equal(0, result.Outcome?.InputsSent);
        Assert.Equal(RefusalCodes.OperationFailed, result.Outcome?.Failure?.Code);
        Assert.False(result.Outcome?.Failure?.ActionDispatched);
    }

    [Fact]
    public void PendingFailureIsNotMisclassifiedAsSafeToRetry()
    {
        var result = ToolRegistry.WithDispatch(
            new McpToolResult
            {
                IsError = true,
                Content = [new() { Type = "text", Text = "Provider has not returned" }],
                Outcome = new ToolOutcome("timed_out_pending", "pending"),
            },
            inputsSent: 0,
            providerPending: true
        );

        Assert.Equal("timed_out_pending", result.Outcome?.Dispatch);
        Assert.Null(result.Outcome?.Failure);
    }

    [Fact]
    public void ARecordedRefusalAfterInputKeepsItsEvidenceWithNoInputTally()
    {
        var result = ToolRegistry.WithDispatch(
            new McpToolResult { IsError = true, Content = [new() { Type = "text", Text = "Interrupted" }] },
            inputsSent: 0,
            refusal: new InputRefusal("target-changed", "Interrupted") { ActionDispatched = true }
        );

        Assert.True(result.Outcome?.Failure?.ActionDispatched);
        Assert.Equal("target-changed", result.Outcome?.Failure?.Code);
        Assert.Equal("partial", result.Outcome?.Dispatch);
    }

    [Theory]
    [InlineData("partial")]
    [InlineData("completed")]
    public async Task UncountedDispatchedFailureNotifiesTheGrant(string dispatch)
    {
        var registry = new ToolRegistry();
        var notified = false;
        registry.FailedAfterInput = (_, _) => notified = true;
        registry.RegisterTool(
            new DelegateTool(
                "fails",
                () =>
                    new McpToolResult
                    {
                        IsError = true,
                        Content = [new() { Type = "text", Text = "Dispatched" }],
                        Outcome = new ToolOutcome(dispatch, "not-tracked") { InputsSent = 0, Status = "failed" },
                    }
            )
        );

        var result = await registry.ExecuteToolAsync("fails", null);

        Assert.True(notified);
        Assert.NotEqual("not-dispatched", result.Outcome?.Dispatch);
        Assert.Null(result.Outcome?.Failure);
    }

    [Fact]
    public async Task NestedCallsCountOnlyTheirOwnInputs()
    {
        using var offlineDesktop = new FakeDesktop().Install();
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
        using var offlineDesktop = new FakeDesktop().Install();
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

        Assert.True(result.IsError); // isError mirrors status: failed.
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
