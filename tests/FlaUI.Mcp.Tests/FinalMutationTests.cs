using System.Drawing;
using System.Text.Json;
using FlaUI.Mcp;
using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>Tests written to kill mutants that survived the final mutation run; see docs/mutation.md.</summary>
[Collection(InputLeaseCollection.Name)]
public sealed class FinalMutationTests
{
    private static JsonElement Args(object value) => JsonSerializer.SerializeToElement(value);

    private static void Denied(Action run)
    {
        MutationGuard.BeforeMutation.Value = () => throw new InvalidOperationException("mutation denied");
        try
        {
            run();
        }
        finally
        {
            MutationGuard.BeforeMutation.Value = null;
        }
    }

    [Fact]
    public void AStandaloneClipboardWriteChecksPermission()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();

        Denied(() => Assert.Throws<InvalidOperationException>(() => GuardedDesktop.SetClipboardText("abc")));

        Assert.Null(desktop.ClipboardWritten);
    }

    [Fact]
    public void AGuardedClipboardWriteRechecksTheGuard()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        using var input = new GuardedInput(FakeDesktop.Target);

        Denied(() =>
        {
            Assert.Throws<InvalidOperationException>(() => input.SetClipboardText("abc"));
            Assert.Throws<InvalidOperationException>(() => input.PreserveAndSetClipboardText("abc"));
        });

        Assert.Null(desktop.ClipboardWritten);
    }

    [Fact]
    public void APointOnNoWindowNamesNoProcess()
    {
        var error = Assert.Throws<InputRefusedException>(() =>
            PointerActions.RequireHit(FakeDesktop.Target, new Point(1, 1), _ => 0)
        );

        Assert.Null(error.Refusal.ForegroundProcess);
    }

    [Fact]
    public void AnActivationDeniedByAnotherProcessSaysWhatToDo()
    {
        var refusal = GuardedInput.DescribeActivationFailure(42, 7, 9, "other");

        Assert.StartsWith(
            "activation-denied: Windows kept 'other' (PID 9) in the foreground instead of target PID 42. ",
            refusal.Message
        );

        Assert.EndsWith(
            "No input sent. Attended: ask the user to click the target window's title bar, then observe before retrying. Unattended: activation cannot be forced.",
            refusal.Message
        );
    }

    [Fact]
    public void ADragWithABadStepCountChangesNothing()
    {
        var desktop = new FakeDesktop { ForegroundWindow = FakeDesktop.OtherHwnd };
        using var installed = desktop.Install();

        Assert.Throws<ArgumentException>(() =>
            PointerActions.Drag(FakeDesktop.Target, Point.Empty, new Point(5, 5), 0)
        );

        Assert.Equal(0, desktop.FocusAttempts);
    }

    [Fact]
    public async Task AReturnedCallWithATrackerIsNotPending()
    {
        var registry = new ToolRegistry { Pending = new PendingInvokeTracker() };
        registry.RegisterTool(
            new Returns("returns", new McpToolResult { Content = [new() { Type = "text", Text = "ok" }] })
        );

        var result = await registry.ExecuteToolAsync("returns", null);

        Assert.Equal("not-tracked", result.Outcome!.Provider);
    }

    [Fact]
    public void AScrollThatDoesNotReturnIsTrackedAndExplained()
    {
        using var offlineDesktop = new FakeDesktop().Install();
        using var hold = new ManualResetEventSlim();
        var pending = new PendingInvokeTracker(_ => 1);
        try
        {
            var error = Assert.Throws<InvalidOperationException>(() =>
                ClickGeometry.Scroll(0, pending, () => { }, hold.Wait, () => Rectangle.Empty)
            );

            Assert.Equal(
                "ScrollIntoView did not return; the app's UI Automation provider may be blocked. Nothing was pressed. Check windows_operation_status before retrying.",
                error.Message
            );
            var entry = JsonSerializer.SerializeToElement(pending.Status()).EnumerateArray().Single();
            Assert.Equal("ScrollIntoView", entry.GetProperty("Description").GetString());
        }
        finally
        {
            hold.Set();
        }
    }

    [Fact]
    public async Task ALastStepStillPendingAfterInputKeepsItsCompletedDispatch()
    {
        var result = await BatchTool.ExecuteRows(
            [Args(new { action = "click", @ref = "w1e1" })],
            (_, _) => Task.FromResult(Step(new("completed", "pending") { InputsSent = 1 }))
        );

        Assert.Equal("completed", result.Outcome!.Dispatch);
        Assert.Equal("pending", result.Outcome.Provider);
    }

    [Fact]
    public async Task APendingStepBeforeOthersMakesTheBatchPartial()
    {
        var result = await BatchTool.ExecuteRows(
            [Args(new { action = "click", @ref = "w1e1" }), Args(new { action = "click", @ref = "w1e2" })],
            (_, _) => Task.FromResult(Step(new("completed", "pending") { InputsSent = 1 }))
        );

        Assert.Equal("partial", result.Outcome!.Dispatch);
        Assert.Equal("pending", result.Outcome.Provider);
    }

    private static McpToolResult Step(ToolOutcome outcome) =>
        new() { Content = [new() { Type = "text", Text = "clicked" }], Outcome = outcome };

    private sealed class Returns(string name, McpToolResult result) : ToolBase
    {
        public override string Name => name;
        public override string Description => "no desktop";
        public override object InputSchema => new { };

        public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments) => Task.FromResult(result);
    }
}
