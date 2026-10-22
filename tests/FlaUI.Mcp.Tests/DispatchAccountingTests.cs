using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Mcp;
using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// inputsSent counts every change a call made to the desktop, and only those: focus changes, pattern
/// calls, clipboard writes and button releases included, no-ops and failed starts excluded.
/// </summary>
[Collection(InputLeaseCollection.Name)]
public sealed class DispatchAccountingTests
{
    private static JsonElement Args(object value) => JsonSerializer.SerializeToElement(value);

    private static AutomationElement Element() =>
        (AutomationElement)RuntimeHelpers.GetUninitializedObject(typeof(AutomationElement));

    /// <summary>Runs <paramref name="action"/> as an operation and returns how many inputs it counted.</summary>
    private static int Counted(Action action)
    {
        var operation = new OperationContext();
        OperationContext.Current.Value = operation;
        try
        {
            action();
        }
        catch (Exception error) when (error is InvalidOperationException or AutomationRefusal) { }
        finally
        {
            OperationContext.Current.Value = null;
        }

        return operation.InputsSent;
    }

    private static (SessionManager Sessions, string Handle) Window()
    {
        var sessions = new SessionManager(null, ProcessPolicy.AllowAll);
        var handle = sessions.PublishWindow(null, FakeDesktop.TargetHwnd, FakeDesktop.TargetPid, FakeDesktop.Target);
        return (sessions, handle);
    }

    // K1: activation and control focus change the desktop, so they count; a no-op does not.

    [Fact]
    public void ActivatingTheTargetCountsAsOneChange()
    {
        var desktop = new FakeDesktop { ForegroundWindow = FakeDesktop.OtherHwnd };
        using var installed = desktop.Install();

        Assert.Equal(1, Counted(() => new GuardedInput(FakeDesktop.Target).Dispose()));
    }

    [Fact]
    public void AnAlreadyActiveTargetCountsNothing()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();

        Assert.Equal(0, Counted(() => new GuardedInput(FakeDesktop.Target).Dispose()));
    }

    [Fact]
    public void ARefusedActivationCountsNothing()
    {
        var desktop = new FakeDesktop { ForegroundWindow = FakeDesktop.OtherHwnd, ActivationAllowed = false };
        using var installed = desktop.Install();

        Assert.Equal(0, Counted(() => new GuardedInput(FakeDesktop.Target).Dispose()));
    }

    [Fact]
    public void FocusingAControlThatLackedFocusCountsAsOneChange()
    {
        var desktop = new FakeDesktop { KeyboardFocus = false };
        using var installed = desktop.Install();

        Assert.Equal(1, Counted(() => new GuardedInput(FakeDesktop.Target, Element(), verifyFocus: true).Dispose()));
    }

    [Fact]
    public void FocusingAControlThatAlreadyHadFocusCountsNothing()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();

        Assert.Equal(0, Counted(() => new GuardedInput(FakeDesktop.Target, Element(), verifyFocus: true).Dispose()));
    }

    [Fact]
    public void ARefusalAfterActivationSaysTheWindowWasActivated()
    {
        var desktop = new FakeDesktop { ForegroundWindow = FakeDesktop.OtherHwnd, KeyboardFocus = false };
        desktop.ElementTakesFocus = false;
        using var installed = desktop.Install();

        var error = Assert.Throws<InvalidOperationException>(() =>
            new GuardedInput(FakeDesktop.Target, Element(), verifyFocus: true)
        );

        Assert.Contains("did not receive verified keyboard focus", error.Message);
        Assert.Contains("the target window was activated first", error.Message);
        Assert.DoesNotContain("no input sent", error.Message);
    }

    // K2: a pattern call still waiting in validation has not been dispatched.

    [Fact]
    public void APatternCallStillValidatingIsTimedOutPendingNotCompleted()
    {
        using var hold = new ManualResetEventSlim();
        var clock = new ManualTimeProvider();
        using var installed = new FakeDesktop { Clock = clock }.Install();
        var grace = TimeSpan.FromMilliseconds(100);
        PatternCallResult result = null!;
        var counted = Counted(() =>
            result = Eventually.PastDeadline(
                clock,
                grace,
                () =>
                    ModalAwareInvoker.Execute(
                        0,
                        "Invoke on 'OK'",
                        () => MutationGuard.Execute(() => hold.Wait(), () => { }),
                        new PendingInvokeTracker(),
                        grace
                    )
            )
        );
        hold.Set();

        Assert.Equal(PatternCallOutcome.StillPending, result.Outcome);
        Assert.Equal(0, counted);
        Assert.Equal("timed_out_pending", ToolOutcome.FromPattern(result).Dispatch);
    }

    [Fact]
    public void APatternCallDispatchedButNotReturnedIsCompletedWithAPendingProvider()
    {
        using var hold = new ManualResetEventSlim();
        using var sent = new ManualResetEventSlim();
        var polls = 0;
        PatternCallResult result = null!;
        var counted = Counted(() =>
            result = ModalAwareInvoker.Execute(
                42,
                "Invoke on 'OK'",
                () =>
                    MutationGuard.Execute(
                        () => { },
                        () =>
                        {
                            sent.Set();
                            hold.Wait();
                        }
                    ),
                new PendingInvokeTracker(_ => 1),
                TimeSpan.Zero,
                // The first poll waits until the call has counted itself, so the result never races it.
                _ =>
                {
                    if (polls++ > 0)
                    {
                        sent.Wait(TimeSpan.FromSeconds(10));
                    }

                    return [];
                }
            )
        );
        hold.Set();

        Assert.Equal(1, counted);
        var outcome = ToolOutcome.FromPattern(result);
        Assert.Equal("completed", outcome.Dispatch);
        Assert.Equal("pending", outcome.Provider);
    }

    [Theory]
    [InlineData(PatternCallOutcome.StillPending, false, "is still waiting on the app")]
    [InlineData(PatternCallOutcome.StillPending, true, "the app's handler is still running")]
    public void AStillPendingClickSaysWhetherItWasSent(PatternCallOutcome outcome, bool dispatched, string text)
    {
        var result = ClickTool.PatternResult(new PatternCallResult(outcome, Dispatched: dispatched), "Invoked OK");

        Assert.Contains(text, result.Content[0].Text);
        Assert.Equal(dispatched ? "completed" : "timed_out_pending", result.Outcome!.Dispatch);
    }

    [Theory]
    [InlineData(false, "SetValue has not been sent yet")]
    [InlineData(true, "SetValue dispatched")]
    public void APendingFillSaysWhetherSetValueWasSent(bool dispatched, string text)
    {
        var result = FillTool.PendingSetValueResult(
            new PatternCallResult(PatternCallOutcome.StillPending, Dispatched: dispatched)
        );

        Assert.StartsWith(text, result.Content[0].Text);
    }

    // K3: the button release goes through the same seam and count as every other input.

    [Fact]
    public void ADragThatFailsMidwayStillReleasesTheButtonThroughTheSeam()
    {
        // Move to the start and press go out; the first path step then sees another window in front.
        var desktop = new FakeDesktop { FocusLostAfter = 2 };
        using var installed = desktop.Install();

        var counted = Counted(() => PointerActions.Drag(FakeDesktop.Target, new Point(10, 10), new Point(50, 10), 4));

        Assert.Equal(3, desktop.Dispatched);
        Assert.Equal(3, counted);
    }

    // K4: the clipboard write is a change of its own.

    [Fact]
    public async Task APasteRefusedAfterTheClipboardWriteCountsItAndSaysSo()
    {
        var desktop = new FakeDesktop { FocusLostOnClipboardWrite = true };
        using var installed = desktop.Install();
        var (sessions, handle) = Window();
        using var _ = sessions;
        var registry = new ToolRegistry();
        registry.RegisterTool(
            new PasteTool(new ElementRegistry(), new PendingInvokeTracker(), ProcessPolicy.AllowAll, sessions)
        );

        var result = await registry.ExecuteToolAsync("windows_paste", Args(new { handle, text = "abc" }));

        Assert.True(result.IsError);
        Assert.Equal("abc", desktop.ClipboardText);
        Assert.Contains("The clipboard now holds that text", result.Content[0].Text);
        Assert.Equal(0, desktop.Dispatched);
        Assert.Equal(1, result.Outcome!.InputsSent);
        Assert.Equal("partial", result.Outcome.Dispatch);
    }

    // K5: count only what happened.

    [Fact]
    public void FocusingTheForegroundWindowCountsNothing()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();

        Assert.Equal(0, Counted(() => WindowMutation.Focus(FakeDesktop.Target, FakeDesktop.TargetHwnd)));
    }

    [Fact]
    public void FocusingABackgroundWindowCountsOne()
    {
        var desktop = new FakeDesktop { ForegroundWindow = FakeDesktop.OtherHwnd };
        using var installed = desktop.Install();

        Assert.Equal(1, Counted(() => WindowMutation.Focus(FakeDesktop.Target, FakeDesktop.TargetHwnd)));
    }

    [Fact]
    public void AProcessThatFailsToStartCountsNothing()
    {
        using var offlineDesktop = new FakeDesktop { StartFails = true }.Install();
        // A missing executable: Windows starts nothing and Process.Start throws.
        var missing = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}", "missing.exe");
        var info = new ProcessStartInfo(missing) { UseShellExecute = false };

        var counted = Counted(() => Assert.Throws<Win32Exception>(() => WindowMutation.Start(info)));

        Assert.Equal(0, counted);
    }

    // K6: a refusal belongs to the step that raised it.

    [Fact]
    public async Task ABatchFailureIsNotBlamedOnAnEarlierStepsRefusal()
    {
        using var offlineDesktop = new FakeDesktop().Install();
        var refusal = new InputRefusal("activation-denied", "activation-denied: test. No input sent.");
        var registry = new ToolRegistry();
        registry.RegisterTool(new DelegateTool("windows_type", () => throw InputRefusedException.Record(refusal)));
        registry.RegisterTool(new DelegateTool("windows_click", () => throw new InvalidOperationException("gone")));
        registry.RegisterTool(new BatchTool(registry));
        var actions = new object[] { new { action = "type", text = "a" }, new { action = "click", @ref = "w1e1" } };

        var result = await registry.ExecuteToolAsync("windows_batch", Args(new { actions, stopOnError = false }));

        var steps = result.Outcome!.Steps!;
        Assert.Equal("activation-denied", steps[0].Outcome!.Failure!.Code);
        Assert.Null(steps[1].Outcome!.Failure);
        Assert.Null(result.Outcome.Failure);
    }

    [Fact]
    public async Task ABatchStoppedByARefusalReportsThatRefusal()
    {
        using var offlineDesktop = new FakeDesktop().Install();
        var refusal = new InputRefusal("activation-denied", "activation-denied: test. No input sent.");
        var registry = new ToolRegistry();
        registry.RegisterTool(new DelegateTool("windows_type", () => throw InputRefusedException.Record(refusal)));
        registry.RegisterTool(new BatchTool(registry));
        var actions = new object[] { new { action = "type", text = "a" } };

        var result = await registry.ExecuteToolAsync("windows_batch", Args(new { actions }));

        Assert.Equal("activation-denied", result.Outcome!.Failure!.Code);
    }

    // K7: the click fallback's refusal text agrees with the Select call it follows.

    [Fact]
    public void ARefusedFallbackClickAfterSelectSaysSelectWasSent()
    {
        var message = ClickTool.ClickFailure("activation-denied: blocked. No input sent.", "X", [], patternSent: true);

        Assert.DoesNotContain("No input sent", message);
        Assert.Contains("Select was sent", message);
    }

    [Fact]
    public void ARefusedClickWithoutAPatternCallKeepsItsText()
    {
        var message = ClickTool.ClickFailure("activation-denied: blocked. No input sent.", "X", [], patternSent: false);

        Assert.Equal("activation-denied: blocked. No input sent.", message);
    }

    private sealed class DelegateTool(string name, Func<McpToolResult> run) : ToolBase
    {
        public override string Name => name;
        public override string Description => "no desktop";
        public override object InputSchema => new { };

        public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments) => Task.FromResult(run());
    }
}
