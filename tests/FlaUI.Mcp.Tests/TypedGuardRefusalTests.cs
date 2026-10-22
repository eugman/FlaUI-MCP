using System.Runtime.CompilerServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// Every guard refusal is typed, with a canonical code a client can act on, and says whether input already went
/// out: the ones raised after input count as dispatched, so a client never replays them.
/// </summary>
[Collection(InputLeaseCollection.Name)]
public sealed class TypedGuardRefusalTests
{
    private static AutomationElement Element() =>
        (AutomationElement)RuntimeHelpers.GetUninitializedObject(typeof(AutomationElement));

    private static void NothingSent(InputRefusedException error, string code)
    {
        Assert.Equal(code, error.Code);
        Assert.Equal(RefusalDisposition.NoInput, error.Disposition);
        Assert.NotEqual(true, error.Refusal.ActionDispatched);
    }

    [Fact]
    public void AStaleTargetIsRefusedAsStale()
    {
        // This process, with a start time it never had and no window: the identity no longer holds.
        var stale = new InputTarget(0, Environment.ProcessId, 1);

        NothingSent(Assert.Throws<InputRefusedException>(stale.EnsureAliveOnWindows), RefusalCodes.TargetStale);
    }

    [Fact]
    public void AWindowWhoseOwnerCannotBeReadIsRefused()
    {
        NothingSent(
            Assert.Throws<InputRefusedException>(() => InputTarget.Capture(0, Environment.ProcessId)),
            RefusalCodes.WindowOwnershipUnknown
        );
    }

    [Fact]
    public void AControlThatDidNotTakeFocusIsRefusedBeforeInput()
    {
        using var installed = new FakeDesktop { KeyboardFocus = false, ElementTakesFocus = false }.Install();

        var error = Assert.Throws<InputRefusedException>(() =>
            new GuardedInput(FakeDesktop.Target, Element(), verifyFocus: true)
        );

        NothingSent(error, RefusalCodes.FocusNotVerified);
    }

    [Fact]
    public void AForegroundProcessOffTheAllowlistIsRefused()
    {
        using var installed = new FakeDesktop().Install();

        var error = Assert.Throws<InputRefusedException>(() =>
            GuardedInput.ForegroundTarget(
                new ProcessPolicy(["some-app-that-is-not-running"]),
                () => new InputTarget(1, FakeDesktop.OtherPid, 0)
            )
        );

        NothingSent(error, RefusalCodes.ProcessNotAllowed);
    }

    [Fact]
    public void ATargetThatChangesBeforeAnyInputIsRefusedWithNothingSent()
    {
        using var installed = new FakeDesktop { TargetEnabled = false }.Install();

        NothingSent(
            Assert.Throws<InputRefusedException>(() => new GuardedInput(FakeDesktop.Target)),
            RefusalCodes.TargetChanged
        );
    }

    [Fact]
    public void ATargetThatChangesAfterInputIsRefusedAsDispatched()
    {
        var desktop = new FakeDesktop { FocusLostAfter = 1 };
        using var installed = desktop.Install();
        var operation = new OperationContext();
        OperationContext.Current.Value = operation;
        try
        {
            using var input = new GuardedInput(FakeDesktop.Target);
            input.Send(() => { });

            var error = Assert.Throws<InputRefusedException>(() => input.Send(() => { }));

            Assert.Equal(RefusalCodes.TargetChanged, error.Code);
            Assert.Equal(RefusalDisposition.Partial, error.Disposition);
            Assert.True(error.Refusal.ActionDispatched);
            Assert.Equal(RefusalCodes.TargetChanged, operation.Refusal?.Code);
            Assert.Equal(1, desktop.Dispatched);
        }
        finally
        {
            OperationContext.Current.Value = null;
        }
    }

    [Fact]
    public void FocusLostAfterCtrlAIsRefusedAsDispatched()
    {
        using var installed = new FakeDesktop { KeyboardFocus = false }.Install();

        var error = Assert.Throws<InputRefusedException>(() =>
            FillTool.RequireStillFocused((Element(), FocusRule.Exact))
        );

        Assert.Equal(RefusalCodes.FocusLost, error.Code);
        Assert.Equal(RefusalDisposition.Partial, error.Disposition);
        Assert.Contains("no further input sent", error.Message);
    }
}
