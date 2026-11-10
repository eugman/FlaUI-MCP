using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// The grant's lock guards its state, not the slow work done on its behalf. A launch, or the start of the input
/// observer (up to 10 s), ran under the lock, so the monitor, Token and Require all waited, and a revocation by the
/// user's own input waited with them.
/// </summary>
public sealed class GrantLockTests
{
    private static readonly TimeSpan Prompt = TimeSpan.FromSeconds(2);

    private sealed class StubObserver : IInputObserver
    {
        public long Sequence { get; set; }
        public InputCounts Counts => new(0, 0);
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    /// <summary>Whether another thread can take the grant's lock now.</summary>
    private static bool Readable(InteractionGrant grant) => Task.Run(() => grant.Code).Wait(Prompt);

    [Fact]
    public void ALaunchDoesNotHoldTheGrantsLock()
    {
        using var grant = new InteractionGrant(() => (1, 2), () => 0);
        grant.Begin("demo", userApproved: true, monitorChanges: false);

        var readable = grant.Launch(() => Readable(grant), _ => 42);

        Assert.True(readable);
        Assert.True(grant.Active);
    }

    [Fact]
    public void ARevocationDuringALaunchStillReturnsWhatStarted()
    {
        long input = 0;
        using var grant = new InteractionGrant(() => (1, 2), () => input);
        grant.Begin("demo", userApproved: true, monitorChanges: false);

        var started = grant.Launch(
            () =>
            {
                // The user takes the machine back while the process is starting.
                input = 1;
                Assert.True(Task.Run(grant.Observe).Wait(Prompt));
                return "process";
            },
            _ => 42
        );

        // The caller owns what started and must be able to close it; the next input is refused.
        Assert.Equal("process", started);
        Assert.False(grant.Active);
        Assert.Equal(InteractionGrant.FocusRevoked, Assert.Throws<InputRefusedException>(grant.Require).Refusal.Code);
    }

    [Fact]
    public void StartingTheObserverDoesNotHoldTheGrantsLock()
    {
        InteractionGrant? grant = null;
        var readable = false;
        grant = new InteractionGrant(
            () => (1, 2),
            createObserver: () =>
            {
                readable = Readable(grant!);
                return new StubObserver();
            }
        );
        using var owned = grant;

        grant.Begin("demo", userApproved: true, monitorChanges: false);

        Assert.True(readable);
        Assert.True(grant.Active);
    }

    [Fact]
    public void ABatchThatBeganWhileTheObserverStartedWinsAndTheLateObserverIsDisposed()
    {
        InteractionGrant? grant = null;
        var starts = 0;
        var late = new StubObserver();
        grant = new InteractionGrant(
            () => (1, 2),
            createObserver: () =>
            {
                if (Interlocked.Increment(ref starts) > 1)
                {
                    return new StubObserver();
                }

                // Another caller begins a batch while this one is still starting its observer.
                Assert.True(
                    Task.Run(() => grant!.Begin("first", userApproved: true, monitorChanges: false)).Wait(Prompt)
                );
                return late;
            }
        );
        using var owned = grant;

        Assert.Throws<InvalidOperationException>(() =>
            grant.Begin("second", userApproved: true, monitorChanges: false)
        );

        Assert.Equal("first", grant.Batch);
        Assert.True(late.Disposed);
    }

    [Fact]
    public void AnObserverThatFailsToStartLeavesNoBatch()
    {
        using var grant = new InteractionGrant(
            () => (1, 2),
            createObserver: () => throw new InvalidOperationException("no hooks")
        );

        Assert.Throws<InvalidOperationException>(() => grant.Begin("demo", userApproved: true, monitorChanges: false));

        Assert.False(grant.Active);
    }
}
