using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Tests.Contract;

/// <summary>
/// A host that declares itself interactive (MutationGuard.RequireHook) fails closed: a path that forgot to install
/// the grant's hook is refused instead of allowed.
/// </summary>
[Collection(InputLeaseCollection.Name)]
public sealed class RequiredHookContractTests
{
    [Fact]
    public void AnInteractiveHostWithNoHookRefusesInput()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        using var required = MutationGuard.RequireHook();

        var error = Assert.Throws<InputRefusedException>(() => new GuardedInput(FakeDesktop.Target));

        Assert.Equal("approval-required", error.Refusal.Code);
        Assert.Equal(0, desktop.Dispatched);
    }

    [Fact]
    public void AnInteractiveHostWithAHookUsesIt()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        using var required = MutationGuard.RequireHook();
        MutationGuard.BeforeMutation.Value = () => { };
        try
        {
            using var input = new GuardedInput(FakeDesktop.Target);
            input.Send(() => { });
        }
        finally
        {
            MutationGuard.BeforeMutation.Value = null;
        }

        Assert.Equal(1, desktop.Dispatched);
    }

    [Fact]
    public void AHostThatDeclaresNothingStillAllowsInputWithoutAHook()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();

        using var input = new GuardedInput(FakeDesktop.Target);
        input.Send(() => { });

        Assert.Equal(1, desktop.Dispatched);
    }
}
