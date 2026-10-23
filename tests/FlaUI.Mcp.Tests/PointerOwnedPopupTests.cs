using System.Drawing;
using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

[Collection(InputLeaseCollection.Name)]
public sealed class PointerOwnedPopupTests
{
    [Fact]
    public void DefaultGuardStillRejectsModelessOwnedPopup()
    {
        const nint popup = 300;
        var desktop = new FakeDesktop
        {
            ForegroundWindow = popup,
            ModalHwnd = popup,
            ActivationAllowed = false,
        };
        using var installed = desktop.Install();

        Assert.Throws<InputRefusedException>(() => new GuardedInput(FakeDesktop.Target));
        Assert.Equal(0, desktop.Dispatched);
    }

    [Fact]
    public void AModelessOwnedPopupInForegroundAcceptsPointerInputWithoutRefocusingItsOwner()
    {
        const nint popup = 300;
        var desktop = new FakeDesktop
        {
            ForegroundWindow = popup,
            ModalHwnd = popup,
            ActivationAllowed = false,
            WindowUnder = _ => popup,
        };
        using var installed = desktop.Install();
        var point = new Point(10, 20);

        var landed = PointerActions.Click(FakeDesktop.Target, () => point);

        Assert.Equal(desktop.Pointer, landed);
        Assert.Equal(0, desktop.FocusAttempts);
        Assert.Equal(1, desktop.Dispatched);
    }
}
