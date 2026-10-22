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
}
