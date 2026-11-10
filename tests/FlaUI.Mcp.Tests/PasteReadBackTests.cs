using System.Runtime.CompilerServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// After Ctrl+V a paste only reads: it polls the control's value for up to two seconds. It no longer holds the
/// machine-wide input lease while it does, and it reads the value and waits through the desktop seam, so the
/// read-back runs offline on a clock the test moves.
/// </summary>
[Collection(InputLeaseCollection.Name)]
public sealed class PasteReadBackTests
{
    private static AutomationElement Element() =>
        (AutomationElement)RuntimeHelpers.GetUninitializedObject(typeof(AutomationElement));

    private static PasteTool Tool() =>
        new(new ElementRegistry(), new PendingInvokeTracker(), ProcessPolicy.AllowAll, null);

    [Fact]
    public void TheReadBackAfterCtrlVDoesNotHoldTheInputLease()
    {
        var desktop = new FakeDesktop { Clock = new ManualTimeProvider() };
        var field = Element();
        desktop.ValueBearing.Add(field);
        desktop.Values[field] = "before";
        var heldWhilePolling = new List<bool>();
        desktop.OnPause = _ =>
        {
            heldWhilePolling.Add(DesktopLease.Held);
            desktop.Values[field] = "before pasted";
        };
        using var installed = desktop.Install();
        using var input = new GuardedInput(FakeDesktop.Target);
        Assert.True(DesktopLease.Held);

        var result = Tool().Paste(input, field, "pasted", "w1e1");

        Assert.Equal([false], heldWhilePolling);
        Assert.Equal("verified", result.Outcome!.Verification);
        Assert.Equal("before pasted", result.Outcome.Proof!.Observed);
    }

    [Fact]
    public void AValueThatNeverChangesEndsTheWaitOnTheDesktopClock()
    {
        var clock = new ManualTimeProvider();
        var desktop = new FakeDesktop { Clock = clock };
        var field = Element();
        desktop.ValueBearing.Add(field);
        desktop.Values[field] = "before";
        using var installed = desktop.Install();
        using var input = new GuardedInput(FakeDesktop.Target);

        var result = Tool().Paste(input, field, "pasted", "w1e1");

        // Two seconds passed on the fake clock only; an unchanged value proves nothing either way.
        Assert.Equal("not-established", result.Outcome!.Verification);
        Assert.Equal(TimeSpan.FromMilliseconds(PasteTool.SettleMilliseconds).Ticks, clock.GetTimestamp());
    }

    [Fact]
    public void AControlWithoutAValueIsNotPolled()
    {
        var desktop = new FakeDesktop { OnPause = _ => Assert.Fail("polled a control with no value") };
        using var installed = desktop.Install();
        using var input = new GuardedInput(FakeDesktop.Target);

        var result = Tool().Paste(input, Element(), "pasted", "w1e1");

        Assert.Equal("unavailable", result.Outcome!.Verification);
    }
}
