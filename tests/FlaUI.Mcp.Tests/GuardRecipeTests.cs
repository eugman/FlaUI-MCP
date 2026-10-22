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
        Assert.Null(desktop.ClipboardText);
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
