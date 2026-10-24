using System.Runtime.CompilerServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Tests;

[Collection(InputLeaseCollection.Name)]
public sealed class PostFocusObservationTests
{
    private static AutomationElement Element() =>
        (AutomationElement)RuntimeHelpers.GetUninitializedObject(typeof(AutomationElement));

    private static int InputsAfterFailure(Action action)
    {
        var operation = new OperationContext();
        OperationContext.Current.Value = operation;
        try
        {
            Assert.Throws<InvalidOperationException>(action);
            return operation.InputsSent;
        }
        finally
        {
            OperationContext.Current.Value = null;
        }
    }

    [Fact]
    public void ActivationCountsWhenForegroundReadFailsAfterFocus()
    {
        var desktop = new FakeDesktop { ForegroundWindow = FakeDesktop.OtherHwnd, FailForegroundAfterFocus = true };
        using var installed = desktop.Install();

        Assert.Equal(1, InputsAfterFailure(() => new GuardedInput(FakeDesktop.Target)));
        Assert.Equal(1, desktop.FocusAttempts);
    }

    [Fact]
    public void ControlFocusCountsWhenPostFocusReadFails()
    {
        var desktop = new FakeDesktop { KeyboardFocus = false, FailKeyboardFocusAfterRequest = true };
        using var installed = desktop.Install();

        Assert.Equal(1, InputsAfterFailure(() => new GuardedInput(FakeDesktop.Target, Element())));
        Assert.Equal(1, desktop.ElementFocusRequests);
    }

    [Fact]
    public void WindowFocusCountsWhenPostFocusStateReadFails()
    {
        var desktop = new FakeDesktop
        {
            ForegroundWindow = FakeDesktop.OtherHwnd,
            FailWindowStateAfterFocus = true,
            ActivationAllowed = false,
        };
        using var installed = desktop.Install();

        Assert.Equal(1, InputsAfterFailure(() => WindowMutation.Focus(FakeDesktop.Target, FakeDesktop.TargetHwnd)));
        Assert.Equal(1, desktop.FocusAttempts);
    }
}
