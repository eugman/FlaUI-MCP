using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Tests.Contract;

/// <summary>
/// The contract of the shared interaction guard: what counts as the user's own input, when the observer stops
/// vouching for it, and when the grant refuses. Each product test project links this file (as it links
/// FakeDesktop.cs), so the guard every host uses is tested in every host.
/// </summary>
[Collection(InputLeaseCollection.Name)]
public sealed class InteractionContractTests
{
    private sealed class DiagnosticObserver : IInputObserver, IInputEventSource
    {
        public long Sequence { get; set; } = 7;
        public InputCounts Counts => new(Sequence, 0);
        public InputEventInfo? LastEvent { get; set; }

        public void Dispose() { }
    }

    [Fact]
    public void RevocationReportsOnlyInputKindTimeAndSequence()
    {
        var observer = new DiagnosticObserver();
        using var grant = new InteractionGrant(() => (1, 2), createObserver: () => observer);
        grant.Begin("demo", userApproved: true, monitorChanges: false);
        Assert.Equal(7, grant.InputEvidence.ApprovedSequence);
        Assert.Null(grant.InputEvidence.RevokedSequence);

        var at = new DateTimeOffset(2026, 10, 25, 22, 20, 55, TimeSpan.Zero);
        observer.Sequence = 8;
        observer.LastEvent = new InputEventInfo(8, at, "keyboard");
        Assert.Equal(InteractionGrant.FocusRevoked, Assert.Throws<InputRefusedException>(grant.Require).Refusal.Code);

        Assert.Equal(8, grant.InputEvidence.RevokedSequence);
        Assert.Equal(observer.LastEvent, grant.InputEvidence.LastEvent);
        Assert.StartsWith("Non-injected keyboard or mouse input observed", grant.Reason);
        Assert.Contains("keyboard", grant.Reason);
        Assert.Contains("2026-10-25T22:20:55", grant.Reason);
        Assert.Contains("approved sequence 7", grant.Reason);
        Assert.Contains("revoked sequence 8", grant.Reason);
        Assert.DoesNotContain("vkCode", grant.Reason);
    }

    [Fact]
    public void AStaleEventIsNotAttributedToTheNewRevocationAfterNormalEnd()
    {
        var observer = new DiagnosticObserver();
        using var grant = new InteractionGrant(() => (1, 2), createObserver: () => observer);
        grant.Begin("demo", userApproved: true, monitorChanges: false);
        grant.End();
        observer.LastEvent = new InputEventInfo(7, DateTimeOffset.UnixEpoch, "keyboard");
        observer.Sequence = 8;

        grant.Observe();

        Assert.Equal(8, grant.InputEvidence.RevokedSequence);
        Assert.Null(grant.InputEvidence.LastEvent);
        Assert.StartsWith("Non-injected keyboard or mouse input observed", grant.Reason);
        Assert.DoesNotContain("1970", grant.Reason);
    }

    [Theory]
    [InlineData(true, 0x00u, true)]
    [InlineData(true, 0x10u, false)] // LLKHF_INJECTED
    [InlineData(true, 0x02u, false)] // LLKHF_LOWER_IL_INJECTED
    [InlineData(false, 0x00u, true)]
    [InlineData(false, 0x01u, false)] // LLMHF_INJECTED
    [InlineData(false, 0x02u, false)] // LLMHF_LOWER_IL_INJECTED
    public void OnlyNonInjectedInputIsTheUsers(bool keyboard, uint flags, bool physical) =>
        Assert.Equal(physical, PhysicalInputObserver.IsPhysical(keyboard, flags));

    [Theory]
    [InlineData(0x0201, true)]
    [InlineData(0x0204, true)]
    [InlineData(0x0207, true)]
    [InlineData(0x020B, true)]
    [InlineData(0x0200, false)] // WM_MOUSEMOVE
    [InlineData(0x0202, false)] // WM_LBUTTONUP
    public void ButtonDownsAreCountedApart(int message, bool down) =>
        Assert.Equal(down, PhysicalInputObserver.IsButtonDown(message));

    [Fact]
    public void AHeartbeatGapLongerThanASecondPoisonsTheObserverForGood()
    {
        var heartbeat = new Heartbeat(0);
        heartbeat.Beat(900);
        Assert.True(heartbeat.Alive(1_800));
        Assert.False(heartbeat.Alive(3_000));
        heartbeat.Beat(3_100);
        Assert.False(heartbeat.Alive(3_200));
    }

    private static InteractionGrant Grant(
        Func<long> input,
        Func<(nint, int)>? foreground = null,
        Func<long>? clock = null
    ) => new(foreground ?? (() => (1, 2)), input, clock);

    [Fact]
    public void WithoutABatchInputIsRefusedAsNeedingApproval()
    {
        using var grant = Grant(() => 0);

        var error = Assert.Throws<InputRefusedException>(grant.Require);
        Assert.Equal(InteractionGrant.ApprovalRequired, error.Refusal.Code);
    }

    [Fact]
    public void TheUsersOwnInputRevokesTheGrant()
    {
        long input = 0;
        using var grant = Grant(() => input);
        grant.Begin("Record the demo", userApproved: true, monitorChanges: false);
        grant.Require();

        input++;

        var error = Assert.Throws<InputRefusedException>(grant.Require);
        Assert.Equal(InteractionGrant.FocusRevoked, error.Refusal.Code);
        Assert.False(grant.Active);
        Assert.True(grant.Token.IsCancellationRequested);
        Assert.True(grant.SafetyToken.IsCancellationRequested);
    }

    [Fact]
    public void AForegroundMissingWithinTheGraceOnlyRefusesThisCall()
    {
        long now = 0;
        nint window = 1;
        using var grant = Grant(() => 0, () => (window, 2), () => now);
        grant.Begin("Record the demo", userApproved: true, monitorChanges: false);
        window = 0;

        Assert.Equal(
            InteractionGrant.FocusUnavailable,
            Assert.Throws<InputRefusedException>(grant.Require).Refusal.Code
        );
        now = InteractionGrant.MissingForegroundGraceMilliseconds;
        Assert.True(grant.Active);
        now = InteractionGrant.MissingForegroundGraceMilliseconds + 1;
        Assert.Equal(InteractionGrant.FocusRevoked, Assert.Throws<InputRefusedException>(grant.Require).Refusal.Code);
        Assert.False(grant.Active);
    }

    [Fact]
    public void ANormalEndKeepsTheSafetyTokenUntilTheUserActs()
    {
        long input = 0;
        using var grant = Grant(() => input);
        grant.Begin("Record the demo", userApproved: true, monitorChanges: false);

        grant.End();
        Assert.True(grant.Token.IsCancellationRequested);
        Assert.False(grant.SafetyToken.IsCancellationRequested);
        input++;
        grant.Observe();
        Assert.True(grant.SafetyToken.IsCancellationRequested);
    }

    [Fact]
    public void AnObserverThatFailsRevokesTheGrant()
    {
        var failing = false;
        using var grant = Grant(() => failing ? throw new InvalidOperationException("observer stopped") : 0);
        grant.Begin("Record the demo", userApproved: true, monitorChanges: false);

        failing = true;

        Assert.Equal(InteractionGrant.FocusRevoked, Assert.Throws<InputRefusedException>(grant.Require).Refusal.Code);
    }

    [Fact]
    public void APausedGrantRefusesUntilObserved()
    {
        using var grant = Grant(() => 0);
        grant.Begin("Record the demo", userApproved: true, monitorChanges: false);
        grant.Pause("a click failed after input");

        Assert.Equal(
            InteractionGrant.PausedAfterFailure,
            Assert.Throws<InputRefusedException>(grant.Require).Refusal.Code
        );
        grant.Observed();
        grant.Require();
    }

    [Fact]
    public void TheGuardRequiresTheGrantBeforeEveryCoreInput()
    {
        long input = 0;
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        using var grant = Grant(() => input);
        grant.Begin("Record the demo", userApproved: true, monitorChanges: false);
        using (grant.Guard())
        {
            using var guarded = new GuardedInput(FakeDesktop.Target);
            guarded.Send(() => { });
            input++;
            Assert.Throws<InputRefusedException>(() => guarded.Send(() => { }));
        }

        Assert.Equal(1, desktop.Dispatched);
        Assert.Null(MutationGuard.BeforeMutation.Value);
    }

    [Fact]
    public void TheInputLeaseSerializesInput()
    {
        using var held = GuardedInput.AcquireLease();

        var busy = Task.Run(() => Assert.Throws<TimeoutException>(() => GuardedInput.AcquireLease()));
        Assert.Contains("busy", busy.GetAwaiter().GetResult().Message);
    }
}
