using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>The input observer's start is bounded: a hook thread that never reports ready fails the start.</summary>
public sealed class ObserverStartTests
{
    [Fact]
    public void AHookThreadThatNeverReportsReadyFailsTheStart()
    {
        using var ready = new ManualResetEventSlim();

        var failure = PhysicalInputObserver.StartFailure(ready, null, TimeSpan.Zero);

        Assert.IsType<TimeoutException>(failure);
        Assert.Contains("did not start", failure!.Message);
    }

    [Fact]
    public void AReadyThreadStartsAndAFailedOneReportsItsFailure()
    {
        using var ready = new ManualResetEventSlim(true);
        var hookError = new InvalidOperationException("no hook");

        Assert.Null(PhysicalInputObserver.StartFailure(ready, null, TimeSpan.Zero));
        Assert.Same(hookError, PhysicalInputObserver.StartFailure(ready, hookError, TimeSpan.Zero));
    }

    [Fact]
    public void ASlowThreadStartDoesNotPoisonTheHeartbeatBeforeTheHooksExist()
    {
        var heartbeat = new Heartbeat(0);

        heartbeat.Restart(5_000);
        heartbeat.Beat(5_400);

        Assert.True(heartbeat.Alive(5_500));
        Assert.Null(heartbeat.Fault);
    }

    [Fact]
    public void SlowHookInstallationStillPoisonsAndARestartDoesNotClearALatch()
    {
        var heartbeat = new Heartbeat(0);
        heartbeat.Restart(5_000);
        heartbeat.Beat(6_500);
        Assert.False(heartbeat.Alive(6_500));

        heartbeat.Restart(7_000);

        Assert.False(heartbeat.Alive(7_100));
    }
}
