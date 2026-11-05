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
        private long sequence = 7;

        public long Sequence
        {
            get => Failure == null ? sequence : throw Failure;
            set => sequence = value;
        }

        public Exception? Failure { get; set; }
        public InputCounts Counts => new(Sequence, 0);
        public InputEventInfo? LastEvent { get; set; }
        public ObserverFault? Fault { get; set; }
        public long ToleratedMoves { get; set; }

        public void Dispose() { }
    }

    [Fact]
    public void AnObserverFaultIsRecordedWithItsCauseGapAndTheEventThatCoincided()
    {
        var observer = new DiagnosticObserver();
        using var grant = new InteractionGrant(() => (1, 2), createObserver: () => observer);
        grant.Begin("demo", userApproved: true, monitorChanges: false);
        var fault = new ObserverFault(ObserverFault.HeartbeatGap, 1_834, GcPauseMs: 3, InputDuringGap: true);
        observer.Failure = new ObserverUnavailableException(fault);
        observer.LastEvent = new InputEventInfo(8, DateTimeOffset.UnixEpoch, "mouse", "move");

        Assert.Equal(InteractionGrant.FocusRevoked, Assert.Throws<InputRefusedException>(grant.Require).Refusal.Code);

        var revocation = grant.LastRevocation!;
        Assert.Equal("Focus/input observation failed", grant.Reason);
        Assert.Equal(GrantRevocation.ObserverFailed, revocation.Cause);
        Assert.Equal(InteractionGrant.FocusRevoked, revocation.Code);
        Assert.Equal(fault, revocation.Fault);
        Assert.Equal(7, revocation.ApprovedSequence);
        Assert.Null(revocation.SnapshotSequence);
        Assert.Equal("mouse", revocation.InputKind);
        Assert.Equal("move", revocation.MouseKind);
        Assert.Contains("heartbeat-gap gap 1834 ms", revocation.Summary());
        Assert.Contains("input during gap", revocation.Summary());

        observer.Fault = fault with { GapAtNextBeatMs = 1_900 };
        Assert.Equal(1_900, grant.LastRevocation!.Fault!.GapAtNextBeatMs);
    }

    [Fact]
    public void AnEventFromBeforeApprovalIsNotAttributedToAFault()
    {
        var observer = new DiagnosticObserver
        {
            LastEvent = new InputEventInfo(7, DateTimeOffset.UnixEpoch, "keyboard"),
        };
        using var grant = new InteractionGrant(() => (1, 2), createObserver: () => observer);
        grant.Begin("demo", userApproved: true, monitorChanges: false);
        observer.Failure = new InvalidOperationException("stopped");

        grant.Observe();

        Assert.Equal(ObserverFault.Other, grant.LastRevocation!.Fault!.Kind);
        Assert.Null(grant.LastRevocation.InputKind);
    }

    [Fact]
    public void EachEndRecordsItsCause()
    {
        var observer = new DiagnosticObserver();
        using var grant = new InteractionGrant(() => (1, 2), createObserver: () => observer);
        Assert.Null(grant.LastRevocation);
        grant.Begin("demo", userApproved: true, monitorChanges: false);
        grant.End();
        Assert.Equal(GrantRevocation.Ended, grant.LastRevocation!.Cause);
        Assert.Equal(InteractionGrant.ApprovalRequired, grant.LastRevocation.Code);

        observer.Sequence = 8;
        observer.LastEvent = new InputEventInfo(8, DateTimeOffset.UnixEpoch, "mouse", "secret");
        grant.Observe();
        // Input after a normal end still stops safety consumers, but it is not a revocation of the batch.
        Assert.True(grant.SafetyToken.IsCancellationRequested);
        Assert.Equal(GrantRevocation.AfterEnd, grant.LastRevocation!.Cause);
        Assert.True(grant.LastRevocation.EndedNormally);
        Assert.Equal(InteractionGrant.FocusRevoked, grant.LastRevocation.Code);
        Assert.Equal(8, grant.LastRevocation.SnapshotSequence);
        Assert.Equal("mouse", grant.LastRevocation.InputKind);
        Assert.Null(grant.LastRevocation.MouseKind);

        grant.Begin("demo", userApproved: true, monitorChanges: false);
        grant.End("Host stopped", safetyEvent: true);
        Assert.Equal(GrantRevocation.HostStopped, grant.LastRevocation!.Cause);
    }

    [Fact]
    public void TheRevocationCountsOnlyTheDriftToleratedDuringTheBatch()
    {
        var observer = new DiagnosticObserver { ToleratedMoves = 3 };
        using var grant = new InteractionGrant(() => (1, 2), createObserver: () => observer);
        grant.Begin("demo", userApproved: true, monitorChanges: false);
        observer.ToleratedMoves = 5;
        observer.Sequence = 8;

        grant.Observe();

        Assert.Equal(2, grant.LastRevocation!.ToleratedMoves);
        Assert.Contains("tolerated mouse drift 2 moves", grant.LastRevocation.Summary());
    }

    [Fact]
    public void AnEndAfterARevocationKeepsTheRevocationsEvidence()
    {
        var observer = new DiagnosticObserver();
        using var grant = new InteractionGrant(() => (1, 2), createObserver: () => observer);
        grant.Begin("demo", userApproved: true, monitorChanges: false);
        observer.Sequence = 8;
        grant.Observe();
        var revoked = grant.LastRevocation!;

        grant.End();
        grant.End("Operation cancelled");

        Assert.Same(revoked, grant.LastRevocation);
        Assert.Equal(GrantRevocation.UserInput, grant.LastRevocation!.Cause);
        Assert.False(grant.LastRevocation.EndedNormally);
        Assert.Equal(InteractionGrant.ApprovalRequired, grant.Code);
    }

    [Theory]
    [InlineData(true, 0x0100, 1)] // a key
    [InlineData(false, 0x0200, 3)] // WM_MOUSEMOVE
    [InlineData(false, 0x0201, 4)] // WM_LBUTTONDOWN
    [InlineData(false, 0x0202, 4)] // WM_LBUTTONUP
    [InlineData(false, 0x020C, 4)] // WM_XBUTTONUP
    [InlineData(false, 0x020A, 5)] // WM_MOUSEWHEEL
    [InlineData(false, 0x020E, 5)] // WM_MOUSEHWHEEL
    [InlineData(false, 0x0210, 2)]
    public void TheHookKeepsTheMouseSubtype(bool keyboard, int message, int kind) =>
        Assert.Equal(kind, PhysicalInputObserver.KindOf(keyboard, message));

    [Fact]
    public void RevocationReportsOnlyInputKindTimeAndSequence()
    {
        var observer = new DiagnosticObserver();
        using var grant = new InteractionGrant(() => (1, 2), createObserver: () => observer);
        grant.Begin("demo", userApproved: true, monitorChanges: false);
        Assert.Equal(7, grant.InputEvidence.ApprovedSequence);
        Assert.Null(grant.InputEvidence.SnapshotSequence);

        var at = new DateTimeOffset(2026, 10, 25, 22, 20, 55, TimeSpan.Zero);
        observer.Sequence = 8;
        observer.LastEvent = new InputEventInfo(8, at, "keyboard");
        Assert.Equal(InteractionGrant.FocusRevoked, Assert.Throws<InputRefusedException>(grant.Require).Refusal.Code);

        Assert.Equal(8, grant.InputEvidence.SnapshotSequence);
        Assert.Equal(observer.LastEvent, grant.InputEvidence.LastEvent);
        Assert.StartsWith("Non-injected keyboard or mouse input observed", grant.Reason);
        Assert.Contains("keyboard", grant.Reason);
        Assert.Contains("2026-10-25T22:20:55", grant.Reason);
        Assert.Contains("approved sequence 7", grant.Reason);
        Assert.Contains("revocation snapshot sequence 8", grant.Reason);
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

        Assert.Equal(8, grant.InputEvidence.SnapshotSequence);
        Assert.Null(grant.InputEvidence.LastEvent);
        Assert.StartsWith("Non-injected keyboard or mouse input observed", grant.Reason);
        Assert.DoesNotContain("1970", grant.Reason);
    }

    [Fact]
    public void EventKindFromAnInjectedObserverCannotAddFreeTextToReasonOrEvidence()
    {
        var observer = new DiagnosticObserver();
        using var grant = new InteractionGrant(() => (1, 2), createObserver: () => observer);
        grant.Begin("demo", userApproved: true, monitorChanges: false);
        observer.Sequence = 8;
        observer.LastEvent = new InputEventInfo(8, DateTimeOffset.UnixEpoch, "keyboard; secret=123");

        grant.Observe();

        Assert.Equal("unknown", grant.InputEvidence.LastEvent?.Kind);
        Assert.Contains("unknown", grant.Reason);
        Assert.DoesNotContain("secret", grant.Reason);
        Assert.Contains("revocation snapshot sequence 8", grant.Reason);
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

    [Fact]
    public void TheLatchKeepsTheGapTheNextBeatTheGcPauseAndWhetherInputCameDuringIt()
    {
        var pause = TimeSpan.FromMilliseconds(10);
        uint? lastInput = 500;
        var heartbeat = new Heartbeat(0, () => pause, () => lastInput);
        heartbeat.Beat(900);
        Assert.Null(heartbeat.Fault);

        pause = TimeSpan.FromMilliseconds(250);
        lastInput = 2_000;
        Assert.False(heartbeat.Alive(3_000));
        Assert.Equal(new ObserverFault(ObserverFault.HeartbeatGap, 2_100, null, 240, true), heartbeat.Fault);

        heartbeat.Beat(3_400);
        heartbeat.Beat(3_500);
        Assert.Equal(2_500, heartbeat.Fault!.GapAtNextBeatMs);
        Assert.Equal(2_100, heartbeat.Fault.GapMs);
    }

    [Fact]
    public void InputBeforeTheGapOrAnUnreadableLastInputIsNotCalledInputDuringIt()
    {
        uint? lastInput = 100;
        var quiet = new Heartbeat(0, lastInputSource: () => lastInput);
        quiet.Beat(900);
        Assert.False(quiet.Alive(2_000));
        Assert.False(quiet.Fault!.InputDuringGap);

        lastInput = null;
        var unread = new Heartbeat(0, lastInputSource: () => lastInput);
        Assert.False(unread.Alive(2_000));
        Assert.Null(unread.Fault!.InputDuringGap);
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

    private sealed class DisplayHold : IDisposable
    {
        public int Held { get; private set; }
        public int Released { get; private set; }

        public IDisposable Acquire()
        {
            Held++;
            return this;
        }

        public void Dispose() => Released++;
    }

    [Fact]
    public void TheDisplayIsHeldOnWhileABatchIsApprovedAndReleasedWhenItEnds()
    {
        long input = 0;
        var display = new DisplayHold();
        using (var grant = new InteractionGrant(() => (1, 2), () => input, keepDisplayOn: display.Acquire))
        {
            Assert.Equal(0, display.Held);
            grant.Begin("Record the demo", userApproved: true, monitorChanges: false);
            Assert.Equal((1, 0), (display.Held, display.Released));

            grant.End();
            Assert.Equal((1, 1), (display.Held, display.Released));

            grant.Begin("Record the demo", userApproved: true, monitorChanges: false);
            input++;
            grant.Observe();
            Assert.False(grant.Active);
            Assert.Equal((2, 2), (display.Held, display.Released));

            grant.Begin("Record the demo", userApproved: true, monitorChanges: false);
            Assert.Equal((3, 2), (display.Held, display.Released));
        }

        Assert.Equal((3, 3), (display.Held, display.Released));
    }

    [Fact]
    public void ADisplayRequestThatFailsDoesNotBlockTheGrant()
    {
        using var grant = new InteractionGrant(
            () => (1, 2),
            () => 0,
            keepDisplayOn: () => throw new InvalidOperationException("PowerCreateRequest failed.")
        );

        grant.Begin("Record the demo", userApproved: true, monitorChanges: false);

        Assert.True(grant.Active);
        grant.Require();
        Assert.Equal("Could not keep the display on: PowerCreateRequest failed.", grant.DisplayRequestProblem);
    }

    [Fact]
    public void TheInputLeaseSerializesInput()
    {
        using var held = GuardedInput.AcquireLease();

        var busy = Task.Run(() => Assert.Throws<TimeoutException>(() => GuardedInput.AcquireLease()));
        Assert.Contains("busy", busy.GetAwaiter().GetResult().Message);
    }
}
