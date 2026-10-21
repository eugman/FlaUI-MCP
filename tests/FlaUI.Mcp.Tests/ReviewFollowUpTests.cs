using System.Text.Json;
using FlaUI.Mcp;
using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>Dispatch reporting fixed after the core stack review.</summary>
[Collection(InputLeaseCollection.Name)]
public sealed class ReviewFollowUpTests
{
    private static JsonElement Args(object value) => JsonSerializer.SerializeToElement(value);

    [Fact]
    public async Task ABatchBlockedAfterEarlierInputIsPartialAndStillPending()
    {
        var registry = new ToolRegistry();
        registry.RegisterTool(new Sends("windows_type"));
        registry.RegisterTool(new Blocked("windows_click"));
        registry.RegisterTool(new BatchTool(registry));
        var actions = new object[] { new { action = "type", text = "a" }, new { action = "click", @ref = "w1e1" } };

        var result = await registry.ExecuteToolAsync("windows_batch", Args(new { actions }));

        Assert.Equal("partial", result.Outcome!.Dispatch);
        Assert.Equal(1, result.Outcome.InputsSent);
        Assert.True(result.Outcome.IsPending);
    }

    [Fact]
    public async Task ABatchBlockedBeforeAnyInputIsStillNotDispatched()
    {
        var registry = new ToolRegistry();
        registry.RegisterTool(new Blocked("windows_click"));
        registry.RegisterTool(new BatchTool(registry));
        var actions = new object[] { new { action = "click", @ref = "w1e1" } };

        var result = await registry.ExecuteToolAsync("windows_batch", Args(new { actions }));

        Assert.Equal("not-dispatched", result.Outcome!.Dispatch);
        Assert.True(result.Outcome.IsPending);
    }

    [Fact]
    public void AModalProvesThePatternCallWentOut()
    {
        using var hold = new ManualResetEventSlim();
        var calls = 0;
        var operation = new OperationContext();
        OperationContext.Current.Value = operation;
        try
        {
            // The call never reaches RecordDispatch, but a dialog opening proves the app received it.
            var result = ModalAwareInvoker.Execute(
                123,
                "Invoke on 'Open...'",
                hold.Wait,
                new PendingInvokeTracker(_ => 1),
                TimeSpan.FromSeconds(5),
                _ => calls++ == 0 ? [] : [new Win32WindowInfo(7, "Open", 123, true, false, false)]
            );

            Assert.Equal(PatternCallOutcome.ModalDetected, result.Outcome);
            Assert.True(result.Dispatched);
            Assert.Equal("completed", ToolOutcome.FromPattern(result).Dispatch);
        }
        finally
        {
            hold.Set();
            OperationContext.Current.Value = null;
        }
    }

    [Fact]
    public void RestoringAMinimizedTargetCountsEvenWhenActivationIsDenied()
    {
        var desktop = new FakeDesktop
        {
            ForegroundWindow = FakeDesktop.OtherHwnd,
            ActivationAllowed = false,
            State = WindowStates.Minimized,
        };
        using var installed = desktop.Install();
        var operation = new OperationContext();
        OperationContext.Current.Value = operation;
        try
        {
            var error = Assert.Throws<InputRefusedException>(() => new GuardedInput(FakeDesktop.Target));

            Assert.Equal("activation-denied", error.Refusal.Code);
            Assert.Contains("No keys or clicks were sent, but the target window was restored first.", error.Message);
            Assert.Equal(1, operation.InputsSent);
        }
        finally
        {
            OperationContext.Current.Value = null;
        }
    }

    [Fact]
    public void ANativeFocusThatOnlyRestoresTheWindowCounts()
    {
        var desktop = new FakeDesktop
        {
            ForegroundWindow = FakeDesktop.OtherHwnd,
            ActivationAllowed = false,
            State = WindowStates.Minimized,
        };
        using var installed = desktop.Install();
        var operation = new OperationContext();
        OperationContext.Current.Value = operation;
        try
        {
            WindowMutation.Focus(FakeDesktop.Target, FakeDesktop.TargetHwnd);

            Assert.Equal(1, operation.InputsSent);
        }
        finally
        {
            OperationContext.Current.Value = null;
        }
    }

    [Fact]
    public void ADropPointOffTheTargetIsAStructuredRefusalThatNamesTheActivation()
    {
        var to = new System.Drawing.Point(50, 10);
        var desktop = new FakeDesktop
        {
            ForegroundWindow = FakeDesktop.OtherHwnd,
            WindowUnder = point => point == to ? FakeDesktop.OtherHwnd : FakeDesktop.TargetHwnd,
        };
        using var installed = desktop.Install();

        var error = Assert.Throws<InputRefusedException>(() =>
            MouseTool.Drag(FakeDesktop.Target, new System.Drawing.Point(10, 10), to, 4)
        );

        Assert.Equal("point-not-on-target", error.Refusal.Code);
        Assert.Equal(FakeDesktop.OtherPid, error.Refusal.ForegroundProcess!.Pid);
        Assert.Equal(FakeDesktop.TargetPid, error.Refusal.Target!.Pid);
        Assert.EndsWith("no keys or clicks were sent, but the target window was activated first.", error.Message);
        Assert.Equal(0, desktop.Dispatched);
    }

    [Fact]
    public void AClickPointRefusalIsStructured()
    {
        var geometry = new ClickGeometry(true, null, new System.Drawing.Rectangle(0, 0, 10, 10), []);

        var error = Assert.Throws<InputRefusedException>(() => ClickPoint.Choose(geometry));

        Assert.Equal("click-point-unavailable", error.Refusal.Code);
        Assert.StartsWith("The element is offscreen", error.Refusal.Message);
    }

    [Fact]
    public void ScrollingIntoViewIsValidatedCountedAndHoldsTheLease()
    {
        var operation = new OperationContext();
        var order = new List<string>();
        var leaseHeld = false;
        OperationContext.Current.Value = operation;
        try
        {
            var scrolled = ClickGeometry.Scroll(
                0,
                new PendingInvokeTracker(),
                () => order.Add("validate"),
                () =>
                {
                    order.Add("scroll");
                    leaseHeld = LeaseHeld();
                }
            );

            Assert.True(scrolled);
            Assert.Equal(["validate", "scroll"], order);
            Assert.Equal(1, operation.InputsSent);
            Assert.True(leaseHeld);
        }
        finally
        {
            OperationContext.Current.Value = null;
        }
    }

    [Fact]
    public void AScrollWhoseTargetFailsValidationIsNotSent()
    {
        var scrolled = false;

        Assert.Throws<InvalidOperationException>(() =>
            ClickGeometry.Scroll(
                0,
                new PendingInvokeTracker(),
                () => throw new InvalidOperationException("stale ref"),
                () => scrolled = true
            )
        );

        Assert.False(scrolled);
    }

    [Fact]
    public void ARefusalAfterScrollingSaysTheViewWasScrolled()
    {
        var error = GuardedInput.ChangedFirst(
            new InvalidOperationException("Point is not on the target window; no input sent."),
            "the view was scrolled"
        );

        Assert.Equal(
            "Point is not on the target window; no keys or clicks were sent, but the view was scrolled first.",
            error.Message
        );
    }

    [Fact]
    public void AFallbackRefusalAfterSelectCarriesTheSameTextAsTheReply()
    {
        var operation = new OperationContext();
        OperationContext.Current.Value = operation;
        try
        {
            var refusal = InputRefusedException.Record(
                new InputRefusal("activation-denied", "activation-denied: blocked. No input sent.")
            );

            ClickTool.NoteSelectSent(refusal);

            Assert.Equal(
                "activation-denied: blocked. No keys or clicks were sent, but Select was sent first.",
                operation.Refusal!.Message
            );
        }
        finally
        {
            OperationContext.Current.Value = null;
        }
    }

    [Theory]
    [InlineData(false, "The clipboard is in use by another process, so nothing was pasted.")]
    [InlineData(
        true,
        "The clipboard is in use by another process, so nothing was pasted. Before this, the target window was activated, so the desktop foreground changed."
    )]
    public async Task ABusyClipboardMentionsActivationOnlyWhenItHappened(bool activated, string message)
    {
        var desktop = new FakeDesktop
        {
            ClipboardBusy = true,
            ForegroundWindow = activated ? FakeDesktop.OtherHwnd : FakeDesktop.TargetHwnd,
        };
        using var installed = desktop.Install();
        using var sessions = new SessionManager(null, ProcessPolicy.AllowAll);
        var handle = sessions.PublishWindow(null, FakeDesktop.TargetHwnd, FakeDesktop.TargetPid, FakeDesktop.Target);
        var registry = new ToolRegistry();
        registry.RegisterTool(
            new PasteTool(new ElementRegistry(), new PendingInvokeTracker(), ProcessPolicy.AllowAll, sessions)
        );

        var result = await registry.ExecuteToolAsync("windows_paste", Args(new { handle, text = "abc" }));

        Assert.Equal(message, result.Content[0].Text);
        Assert.Equal(activated ? 1 : 0, result.Outcome!.InputsSent);
    }

    private static bool LeaseHeld()
    {
        var gate = (SemaphoreSlim)
            typeof(GuardedInput)
                .GetField(
                    "InputGate",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static
                )!
                .GetValue(null)!;
        return gate.CurrentCount == 0;
    }

    private sealed class Sends(string name) : ToolBase
    {
        public override string Name => name;
        public override string Description => "no desktop";
        public override object InputSchema => new { };

        public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
        {
            MutationGuard.RecordDispatch();
            return Task.FromResult(TextResult("typed"));
        }
    }

    private sealed class Blocked(string name) : ToolBase
    {
        public override string Name => name;
        public override string Description => "no desktop";
        public override object InputSchema => new { };

        public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments) =>
            Task.FromResult(BlockedResult(new PendingInvokeTracker(_ => 1).Begin(42, "Invoke on 'Open...'")));
    }
}
