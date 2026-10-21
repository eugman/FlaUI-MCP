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
        catch (InvalidOperationException) { }
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
        PatternCallResult result = null!;
        var counted = Counted(() =>
            result = ModalAwareInvoker.Execute(
                0,
                "Invoke on 'OK'",
                () => MutationGuard.Execute(() => hold.Wait(), () => { }),
                new PendingInvokeTracker(),
                TimeSpan.FromMilliseconds(100)
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
        PatternCallResult result = null!;
        var counted = Counted(() =>
            result = ModalAwareInvoker.Execute(
                0,
                "Invoke on 'OK'",
                () => MutationGuard.Execute(() => { }, () => hold.Wait()),
                new PendingInvokeTracker(),
                TimeSpan.FromMilliseconds(100)
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

        var counted = Counted(() => MouseTool.Drag(FakeDesktop.Target, new Point(10, 10), new Point(50, 10), 4));

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

    private sealed class DelegateTool(string name, Func<McpToolResult> run) : ToolBase
    {
        public override string Name => name;
        public override string Description => "no desktop";
        public override object InputSchema => new { };

        public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments) => Task.FromResult(run());
    }
}
