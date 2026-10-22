using System.Drawing;
using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Tests.Contract;

/// <summary>
/// When Windows refuses to bring a host's window forward, the host asks the user to click it. During that prompt
/// the user's click must not revoke the grant; afterwards the grant continues only if that click, and nothing else,
/// happened inside the host's window. Otherwise it is revoked with activation-denied, and stays revoked.
/// </summary>
[Collection(InputLeaseCollection.Name)]
public sealed class ActivationContractTests
{
    private static readonly Rectangle Window = new(100, 100, 800, 600);

    private sealed class Counter
    {
        public InputCounts Counts { get; set; }

        public long Sequence => Counts.Keys + Counts.ButtonDowns;
    }

    private static (InteractionGrant Grant, Counter Input) Begun(Func<long>? clock = null)
    {
        var input = new Counter();
        var grant = new InteractionGrant(() => (1, 2), () => input.Sequence, clock, counts: () => input.Counts);
        grant.Begin("Record the demo", userApproved: true, monitorChanges: false);
        return (grant, input);
    }

    [Fact]
    public void AClickInsideTheWindowDuringThePromptKeepsTheGrant()
    {
        var (grant, input) = Begun();
        using var _ = grant;
        grant.BeginPrompt();
        input.Counts = new InputCounts(0, 1, new Point(400, 300));
        grant.Observe();
        Assert.True(grant.Active);

        grant.Rebaseline(Window);

        grant.Require();
    }

    [Theory]
    [InlineData(1, 1, 400, 300, "keyboard input")]
    [InlineData(0, 0, 400, 300, "without a click")]
    [InlineData(0, 1, 50, 50, "not inside")]
    public void AnythingButAClickInsideTheWindowRevokesWithActivationDenied(
        int keys,
        int clicks,
        int x,
        int y,
        string reason
    )
    {
        var (grant, input) = Begun();
        using var _ = grant;
        grant.BeginPrompt();
        input.Counts = new InputCounts(keys, clicks, new Point(x, y));

        var error = Assert.Throws<InputRefusedException>(() => grant.Rebaseline(Window));

        Assert.Equal(RefusalCodes.ActivationDenied, error.Refusal.Code);
        Assert.Contains(reason, error.Message);
        Assert.False(grant.Active);
    }

    [Fact]
    public void ARevokedGrantStaysRevoked()
    {
        var (grant, input) = Begun();
        using var _ = grant;
        grant.BeginPrompt();
        input.Counts = new InputCounts(1, 0, null);
        Assert.Throws<InputRefusedException>(() => grant.Rebaseline(Window));

        Assert.Throws<InputRefusedException>(() => grant.Rebaseline(Window));
        Assert.Equal(RefusalCodes.ActivationDenied, Assert.Throws<InputRefusedException>(grant.Require).Refusal.Code);
    }

    [Fact]
    public void APromptNobodyAnswersIsRevokedAfterItsWindow()
    {
        long now = 0;
        var (grant, _) = Begun(() => now);
        using var __ = grant;
        grant.BeginPrompt(TimeSpan.FromSeconds(120));

        now = 120_000;
        grant.Observe();
        Assert.True(grant.Active);
        now = 120_001;
        grant.Observe();

        Assert.False(grant.Active);
        Assert.Equal(RefusalCodes.ActivationDenied, grant.Code);
    }

    [Fact]
    public void InputIsRefusedWhileThePromptIsOpen()
    {
        var (grant, _) = Begun();
        using var __ = grant;
        grant.BeginPrompt();

        Assert.Equal("activation-pending", Assert.Throws<InputRefusedException>(grant.Require).Refusal.Code);
    }

    [Fact]
    public void AnEndCanRecordActivationDenied()
    {
        var (grant, _) = Begun();
        using var __ = grant;

        grant.End("the user did not click the window", code: RefusalCodes.ActivationDenied);

        Assert.Equal(RefusalCodes.ActivationDenied, Assert.Throws<InputRefusedException>(grant.Require).Refusal.Code);
    }

    [Fact]
    public void ABatchWithNoInputSourceIsRefusedRatherThanUnwatched()
    {
        using var grant = new InteractionGrant(() => (1, 2));

        Assert.Throws<ArgumentException>(() => grant.Begin("Record", userApproved: true, monitorChanges: false));
    }

    [Fact]
    public void TheUsersInputKeepsCamtasiasReasonText()
    {
        long input = 0;
        using var grant = new InteractionGrant(() => (1, 2), () => input);
        grant.Begin("Record", userApproved: true, monitorChanges: false);
        input++;

        var error = Assert.Throws<InputRefusedException>(grant.Require);

        Assert.Equal(RefusalCodes.FocusRevoked, error.Refusal.Code);
        Assert.Equal(
            "Interaction approval required: Non-injected keyboard or mouse input observed; ask the user before resuming",
            error.Message
        );
    }

    [Fact]
    public void ConstructingAGrantDeclaresTheHostInteractive()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        Assert.False(MutationGuard.HookRequired);
        using (new InteractionGrant(() => (1, 2), () => 0))
        {
            Assert.True(MutationGuard.HookRequired);
            Assert.Throws<InputRefusedException>(() => new GuardedInput(FakeDesktop.Target));
        }

        Assert.False(MutationGuard.HookRequired);
        Assert.Equal(0, desktop.Dispatched);
    }

    [Fact]
    public void ARebaselineWithNoOpenPromptIsRefused()
    {
        var (grant, _) = Begun();
        using var __ = grant;

        Assert.Throws<InputRefusedException>(() => grant.Rebaseline(Window));
        Assert.True(grant.Active);
    }

    [Fact]
    public void ARebaselineAfterThePromptExpiredRevokes()
    {
        long now = 0;
        var (grant, input) = Begun(() => now);
        using var _ = grant;
        grant.BeginPrompt(TimeSpan.FromSeconds(1));
        input.Counts = new InputCounts(0, 1, new Point(400, 300));
        now = 1_001;

        var error = Assert.Throws<InputRefusedException>(() => grant.Rebaseline(Window));

        Assert.Equal(RefusalCodes.ActivationDenied, error.Refusal.Code);
        Assert.False(grant.Active);
    }

    [Fact]
    public void ASecondPromptWhileOneIsOpenIsRefused()
    {
        var (grant, _) = Begun();
        using var __ = grant;
        grant.BeginPrompt();

        Assert.Equal(
            InteractionGrant.ActivationPending,
            Assert.Throws<InputRefusedException>(() => grant.BeginPrompt()).Refusal.Code
        );
    }

    [Fact]
    public void ALaunchIsRefusedWhileAPromptIsOpen()
    {
        var (grant, _) = Begun();
        using var __ = grant;
        grant.BeginPrompt();
        var launched = false;

        Assert.Throws<InputRefusedException>(() => grant.Launch(() => launched = true, _ => 1));
        Assert.False(launched);
    }
}
