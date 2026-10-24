using System.Drawing;
using System.Runtime.CompilerServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// Every path that changes the app runs the same guards as <see cref="GuardedDesktop.CallPattern"/> and
/// <see cref="GuardedInput"/>: the integrity check before a pattern call, and a fresh permission check right
/// before the clipboard is replaced. Each refusal comes with no pattern call, write or input.
/// </summary>
[Collection(InputLeaseCollection.Name)]
public sealed class GuardRecipeTests
{
    private static AutomationElement Element() =>
        (AutomationElement)RuntimeHelpers.GetUninitializedObject(typeof(AutomationElement));

    private static FakeDesktop Elevated() => new() { TargetIntegrity = new(ProcessIntegrity.High, false) };

    [Fact]
    public void AClickPatternCallIntoAHigherIntegrityAppIsRefused()
    {
        var desktop = Elevated();
        using var installed = desktop.Install();
        var tool = new ClickTool(new ElementRegistry());

        var error = Assert.Throws<InputRefusedException>(() =>
            tool.RunPattern(Element(), "invoke", FakeDesktop.TargetPid, "OK", () => { })
        );

        Assert.Equal("integrity-blocked", error.Refusal.Code);
        Assert.Equal(0, desktop.PatternCalls);
    }

    [Fact]
    public void AFillSetValueIntoAHigherIntegrityAppIsRefused()
    {
        var desktop = Elevated();
        using var installed = desktop.Install();
        var applied = false;

        var error = Assert.Throws<InputRefusedException>(() =>
            new FillTool(new ElementRegistry()).SetValue(FakeDesktop.TargetPid, () => applied = true, () => { })
        );

        Assert.Equal("integrity-blocked", error.Refusal.Code);
        Assert.False(applied);
        Assert.Equal(0, desktop.PatternCalls);
    }

    [Fact]
    public void AScrollIntoViewOfAHigherIntegrityAppIsRefused()
    {
        var desktop = Elevated();
        using var installed = desktop.Install();
        var scrolled = false;

        var error = Assert.Throws<InputRefusedException>(() =>
            ClickGeometry.Scroll(
                FakeDesktop.TargetPid,
                new PendingInvokeTracker(),
                () => { },
                () => scrolled = true,
                () => Rectangle.Empty
            )
        );

        Assert.Equal("integrity-blocked", error.Refusal.Code);
        Assert.False(scrolled);
        Assert.Equal(0, desktop.PatternCalls);
    }

    [Fact]
    public void DeniedActivationPreventsScrollBeforeAnyInput()
    {
        var desktop = new FakeDesktop { ForegroundWindow = FakeDesktop.OtherHwnd, ActivationAllowed = false };
        using var installed = desktop.Install();
        var operation = new OperationContext();
        OperationContext.Current.Value = operation;
        var scrolled = false;
        try
        {
            var error = Assert.Throws<InputRefusedException>(() =>
                ClickGeometry.Scroll(
                    FakeDesktop.TargetPid,
                    new PendingInvokeTracker(),
                    () => { },
                    () => scrolled = true,
                    () => Rectangle.Empty,
                    target: FakeDesktop.Target
                )
            );

            Assert.Equal(RefusalCodes.ActivationDenied, error.Refusal.Code);
            Assert.False(scrolled);
            Assert.Equal(0, operation.InputsSent);
            Assert.Equal(0, desktop.PatternCalls);
        }
        finally
        {
            OperationContext.Current.Value = null;
        }
    }

    [Fact]
    public void ForegroundDriftAfterPreflightPreventsScrollInsideTheLease()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        var scrolled = false;

        var error = Assert.Throws<InputRefusedException>(() =>
            ClickGeometry.Scroll(
                FakeDesktop.TargetPid,
                new PendingInvokeTracker(),
                () => desktop.ForegroundWindow = FakeDesktop.OtherHwnd,
                () => scrolled = true,
                () => Rectangle.Empty,
                target: FakeDesktop.Target
            )
        );

        Assert.Equal(RefusalCodes.ActivationDenied, error.Refusal.Code);
        Assert.False(scrolled);
        Assert.Equal(0, desktop.PatternCalls);
    }

    [Fact]
    public void SuccessfulActivationIsCountedBeforeScroll()
    {
        var desktop = new FakeDesktop { ForegroundWindow = FakeDesktop.OtherHwnd };
        using var installed = desktop.Install();
        var operation = new OperationContext();
        OperationContext.Current.Value = operation;
        var bounds = Rectangle.Empty;
        try
        {
            var note = ClickGeometry.Scroll(
                FakeDesktop.TargetPid,
                new PendingInvokeTracker(),
                () => { },
                () => bounds = new Rectangle(1, 1, 10, 10),
                () => bounds,
                target: FakeDesktop.Target
            );

            Assert.Equal("the view was scrolled", note);
            Assert.Equal(2, operation.InputsSent);
            Assert.Equal(FakeDesktop.TargetHwnd, desktop.Foreground());
        }
        finally
        {
            OperationContext.Current.Value = null;
        }
    }

    [Fact]
    public void FailedBoundsReadAfterActivationPreservesTheFocusChange()
    {
        var desktop = new FakeDesktop { ForegroundWindow = FakeDesktop.OtherHwnd };
        using var installed = desktop.Install();
        var operation = new OperationContext();
        OperationContext.Current.Value = operation;
        var scrolled = false;
        try
        {
            var error = Assert.Throws<InvalidOperationException>(() =>
                ClickGeometry.Scroll(
                    FakeDesktop.TargetPid,
                    new PendingInvokeTracker(),
                    () => { },
                    () => scrolled = true,
                    () => throw new InvalidOperationException("bounds unreadable"),
                    target: FakeDesktop.Target
                )
            );

            Assert.Contains("bounds unreadable", error.Message);
            Assert.Contains("Before this", error.Message);
            Assert.False(scrolled);
            Assert.Equal(1, operation.InputsSent);
        }
        finally
        {
            OperationContext.Current.Value = null;
        }
    }

    [Fact]
    public void FailedBoundsReadAfterSentScrollCountsTheDispatch()
    {
        using var installed = new FakeDesktop().Install();
        var operation = new OperationContext();
        OperationContext.Current.Value = operation;
        var reads = 0;
        var scrolled = false;
        try
        {
            Assert.Throws<InvalidOperationException>(() =>
                ClickGeometry.Scroll(
                    FakeDesktop.TargetPid,
                    new PendingInvokeTracker(),
                    () => { },
                    () => scrolled = true,
                    () => ++reads == 1 ? Rectangle.Empty : throw new InvalidOperationException("bounds unreadable")
                )
            );

            Assert.True(scrolled);
            Assert.Equal(1, operation.InputsSent);
        }
        finally
        {
            OperationContext.Current.Value = null;
        }
    }

    [Fact]
    public void APermissionTripAfterTheGuardIsBuiltLeavesTheClipboardAlone()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        var tool = new PasteTool(new ElementRegistry(), new PendingInvokeTracker(), ProcessPolicy.AllowAll, null);
        using var input = new GuardedInput(FakeDesktop.Target);
        using var trip = PermissionTrip.DenyAfter(0);

        var error = Assert.Throws<InvalidOperationException>(() =>
            tool.Paste(input, null, "secret", "focused element")
        );

        Assert.Equal(PermissionTrip.Message, error.Message);
        Assert.Null(desktop.ClipboardWritten);
        Assert.Equal(0, desktop.Dispatched);
    }

    [Fact]
    public void ATripCountsTheChecksItAllowed()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        using var trip = PermissionTrip.DenyAfter(2);
        using var input = new GuardedInput(FakeDesktop.Target);

        Assert.Equal(2, trip.Checks);
        Assert.Throws<InvalidOperationException>(() => input.Send(() => { }));
        Assert.Equal(0, desktop.Dispatched);
    }
}
