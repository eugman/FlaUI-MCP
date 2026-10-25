using System.Runtime.CompilerServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>The refusal and dispatch branches of <see cref="GuardedInput"/> that the tool tests do not reach.</summary>
[Collection(InputLeaseCollection.Name)]
public sealed class GuardedInputBranchTests
{
    private const nint ModalHwnd = 300;

    [Theory]
    [InlineData(300, true, 1, true)]
    [InlineData(300, true, 2, false)]
    [InlineData(300, false, 1, false)]
    [InlineData(0, true, 1, false)]
    public void ClickHitAcceptsOnlyASameProcessOwnedPopup(int hit, bool owned, int processId, bool expected)
    {
        var target = new InputTarget(100, 1, 1);
        Assert.Equal(expected, InputTarget.SameProcessOwnedPopup(target, hit, (_, _) => owned, _ => processId));
    }

    private static AutomationElement Element() =>
        (AutomationElement)RuntimeHelpers.GetUninitializedObject(typeof(AutomationElement));

    private static void AssertLeaseReleased()
    {
        using var lease = GuardedInput.AcquireLease();
    }

    [Fact]
    public void VerifiedFocusIsRefusedWhenTheControlDoesNotHoldKeyboardFocus()
    {
        var desktop = new FakeDesktop { KeyboardFocus = false, ElementTakesFocus = false };
        using var installed = desktop.Install();

        var error = Assert.Throws<InputRefusedException>(() =>
            new GuardedInput(FakeDesktop.Target, Element(), verifyFocus: true)
        );

        Assert.Contains("did not receive verified keyboard focus", error.Message);
        Assert.Equal(1, desktop.ElementFocusRequests);
        Assert.Equal(0, desktop.Dispatched);
        AssertLeaseReleased();
    }

    [Fact]
    public void VerifiedFocusWithoutAnElementIsRefused()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();

        var error = Assert.Throws<InputRefusedException>(() => new GuardedInput(FakeDesktop.Target, verifyFocus: true));

        Assert.Contains("did not receive verified keyboard focus", error.Message);
        AssertLeaseReleased();
    }

    [Fact]
    public void TheIntendedControlIsFocusedBeforeInput()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();

        using (var input = new GuardedInput(FakeDesktop.Target, Element(), verifyFocus: true))
        {
            input.Send(() => { });
        }

        Assert.Equal(1, desktop.ElementFocusRequests);
        Assert.Equal(1, desktop.Dispatched);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(64, 1)]
    [InlineData(65, 2)]
    [InlineData(129, 3)]
    public void LongTextIsSentInChunksOf64(int length, int chunks)
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();

        using (var input = new GuardedInput(FakeDesktop.Target))
        {
            input.Type(new string('a', length));
        }

        Assert.Equal(chunks, desktop.Dispatched);
    }

    [Fact]
    public void TypingStopsAtTheChunkAfterFocusMoves()
    {
        var desktop = new FakeDesktop { FocusLostAfter = 1 };
        using var installed = desktop.Install();
        using var input = new GuardedInput(FakeDesktop.Target);

        Assert.Throws<InputRefusedException>(() => input.Type(new string('a', 200)));

        Assert.Equal(1, desktop.Dispatched);
    }

    [Fact]
    public void NoForegroundWindowIsRefusedAsDesktopUnavailable()
    {
        var desktop = new FakeDesktop { ForegroundWindow = 0, ActivationAllowed = false };
        using var installed = desktop.Install();

        var error = Assert.Throws<InputRefusedException>(() => new GuardedInput(FakeDesktop.Target));

        Assert.Equal("desktop-unavailable", error.Refusal.Code);
        Assert.Equal(1, desktop.FocusAttempts);
        AssertLeaseReleased();
    }

    [Fact]
    public void ADisabledTargetActivatesItsModalDialogAndInputGoesThere()
    {
        var desktop = new FakeDesktop
        {
            ForegroundWindow = FakeDesktop.OtherHwnd,
            TargetEnabled = false,
            ModalHwnd = ModalHwnd,
        };
        using var installed = desktop.Install();

        using (var input = new GuardedInput(FakeDesktop.Target))
        {
            input.Send(() => { });
        }

        Assert.Equal(ModalHwnd, desktop.ForegroundWindow);
        Assert.Equal(1, desktop.Dispatched);
    }

    [Fact]
    public void ADisabledTargetWithoutAnOwnedDialogTakesNoInput()
    {
        var desktop = new FakeDesktop { TargetEnabled = false };
        using var installed = desktop.Install();

        Assert.Throws<InputRefusedException>(() => new GuardedInput(FakeDesktop.Target));

        Assert.Equal(0, desktop.Dispatched);
        AssertLeaseReleased();
    }
}
