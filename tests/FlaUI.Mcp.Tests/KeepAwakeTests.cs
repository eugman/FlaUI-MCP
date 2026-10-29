using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Tests;

public class KeepAwakeTests
{
    [Fact]
    public void Poke_FirstCall_AcquiresOnce()
    {
        var acquired = 0;
        var released = 0;
        using var keepAwake = new KeepAwake(TimeSpan.FromMinutes(5), () => acquired++, () => released++);

        keepAwake.Poke();
        keepAwake.Poke();
        keepAwake.Poke();

        Assert.Equal(1, acquired);
        Assert.Equal(0, released);
        Assert.True(keepAwake.IsActive);
    }

    [Fact]
    public void Poke_AfterIdlePeriod_Releases()
    {
        var clock = new ManualTimeProvider();
        var released = 0;
        using var keepAwake = new KeepAwake(TimeSpan.FromMilliseconds(50), () => { }, () => released++, time: clock);

        keepAwake.Poke();
        clock.Advance(TimeSpan.FromMilliseconds(49));
        clock.WaitForCallbacks();
        Assert.Equal(0, released);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        clock.WaitForCallbacks();

        Assert.Equal(1, released);
        Assert.False(keepAwake.IsActive);
    }

    [Fact]
    public void Poke_AfterRelease_AcquiresAgain()
    {
        var clock = new ManualTimeProvider();
        var acquired = 0;
        var released = 0;
        using var keepAwake = new KeepAwake(
            TimeSpan.FromMilliseconds(50),
            () => acquired++,
            () => released++,
            time: clock
        );

        keepAwake.Poke();
        clock.Advance(TimeSpan.FromMilliseconds(50));
        clock.WaitForCallbacks();
        Assert.Equal(1, released);

        keepAwake.Poke();

        Assert.Equal(2, acquired);
        Assert.True(keepAwake.IsActive);
    }

    [Fact]
    public void Timer_WhenReleaseFails_KeepsActiveAndRetries()
    {
        var clock = new ManualTimeProvider();
        var attempts = 0;
        using var keepAwake = new KeepAwake(
            TimeSpan.FromMilliseconds(50),
            () => { },
            () =>
            {
                attempts++;
                if (attempts == 1)
                {
                    throw new System.ComponentModel.Win32Exception();
                }
            },
            time: clock
        );

        keepAwake.Poke();
        clock.Advance(TimeSpan.FromMilliseconds(50));
        clock.WaitForCallbacks();
        Assert.True(keepAwake.IsActive);

        clock.Advance(TimeSpan.FromMilliseconds(50));
        clock.WaitForCallbacks();
        Assert.Equal(2, attempts);
        Assert.False(keepAwake.IsActive);
    }

    [Fact]
    public void Poke_ExtendsHold_NoReleaseWhileActivityContinues()
    {
        var clock = new ManualTimeProvider();
        var released = 0;
        using var keepAwake = new KeepAwake(TimeSpan.FromMilliseconds(200), () => { }, () => released++, time: clock);

        // Poke at intervals inside the hold; together they run well past one hold.
        for (var i = 0; i < 5; i++)
        {
            keepAwake.Poke();
            clock.Advance(TimeSpan.FromMilliseconds(150));
            clock.WaitForCallbacks();
            Assert.True(released == 0, $"Released too early on iteration {i}.");
        }

        Assert.True(keepAwake.IsActive);
        clock.Advance(TimeSpan.FromMilliseconds(50));
        clock.WaitForCallbacks();
        Assert.Equal(1, released);
    }

    [Fact]
    public void Dispose_WhileActive_Releases()
    {
        var released = 0;
        var keepAwake = new KeepAwake(TimeSpan.FromMinutes(5), () => { }, () => released++);

        keepAwake.Poke();
        keepAwake.Dispose();

        Assert.Equal(1, released);
    }

    [Fact]
    public void Dispose_WhileInactive_DoesNotRelease()
    {
        var released = 0;
        var keepAwake = new KeepAwake(TimeSpan.FromMinutes(5), () => { }, () => released++);

        keepAwake.Dispose();

        Assert.Equal(0, released);
    }

    [Fact]
    public void Poke_AfterDispose_IsIgnored()
    {
        var acquired = 0;
        var keepAwake = new KeepAwake(TimeSpan.FromMinutes(5), () => acquired++, () => { });

        keepAwake.Dispose();
        keepAwake.Poke();

        Assert.Equal(0, acquired);
        Assert.False(keepAwake.IsActive);
    }

    [Fact]
    public void Constructor_NonPositiveHoldDuration_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new KeepAwake(TimeSpan.Zero, () => { }, () => { }));
    }

    [Fact]
    public void Dispose_DisposesOwnedResource()
    {
        var resource = new TrackingDisposable();
        var keepAwake = new KeepAwake(TimeSpan.FromMinutes(5), () => { }, () => { }, resource);

        keepAwake.Dispose();

        Assert.True(resource.Disposed);
    }

    [Fact]
    public void CreateDisplayKeepAwake_OnWindows_CreatesWorkingInstance()
    {
        // Exercises the real PowerCreateRequest/PowerSetRequest/PowerClearRequest
        // round-trip. Setting and clearing an availability request has no lasting
        // side effects.
        using var keepAwake = KeepAwake.CreateDisplayKeepAwake(TimeSpan.FromMinutes(5), "FlaUI-MCP unit test");

        Assert.NotNull(keepAwake);
        keepAwake!.Poke();
        Assert.True(keepAwake.IsActive);
    }

    [Fact]
    public void Poke_WhenDisplayRequestFails_DoesNotReportActive()
    {
        using var request = new PowerAvailabilityRequest(() => false, () => true, () => true, () => true, () => true);
        using var keepAwake = new KeepAwake(TimeSpan.FromMinutes(5), request.Set, request.Clear);

        keepAwake.Poke();

        Assert.False(keepAwake.IsActive);
    }

    [Fact]
    public void Poke_WhenSystemRequestFails_RollsBackDisplayRequest()
    {
        var displaySets = 0;
        var displayClears = 0;
        using var request = new PowerAvailabilityRequest(
            () =>
            {
                displaySets++;
                return true;
            },
            () => false,
            () =>
            {
                displayClears++;
                return true;
            },
            () => true,
            () => true
        );
        using var keepAwake = new KeepAwake(TimeSpan.FromMinutes(5), request.Set, request.Clear);

        keepAwake.Poke();

        Assert.False(keepAwake.IsActive);
        Assert.Equal(1, displaySets);
        Assert.Equal(1, displayClears);
    }

    [Fact]
    public void Set_WhenRollbackFails_ClosesTheNativeHandle()
    {
        var closes = 0;
        using var request = new PowerAvailabilityRequest(
            () => true,
            () => false,
            () => false,
            () => true,
            () =>
            {
                closes++;
                return true;
            }
        );

        Assert.Throws<System.ComponentModel.Win32Exception>(() => request.Set());

        Assert.Equal(1, closes);
        Assert.Throws<ObjectDisposedException>(() => request.Set());
    }

    [Fact]
    public void Poke_WhenRollbackAndHandleCloseFail_RetriesPendingRelease()
    {
        var clock = new ManualTimeProvider();
        var displayClears = 0;
        var closes = 0;
        using var request = new PowerAvailabilityRequest(
            () => true,
            () => false,
            () => ++displayClears > 1,
            () => true,
            () => ++closes > 1
        );
        using var keepAwake = new KeepAwake(
            TimeSpan.FromMilliseconds(50),
            request.Set,
            request.Clear,
            ownedResource: request,
            time: clock,
            isAcquired: () => request.IsFullySet
        );

        keepAwake.Poke();

        Assert.False(keepAwake.IsActive);
        Assert.Equal(1, clock.PendingTimers);
        clock.Advance(TimeSpan.FromMilliseconds(50));
        clock.WaitForCallbacks();
        Assert.Equal(2, displayClears);
        Assert.Equal(0, clock.PendingTimers);
    }

    [Fact]
    public void Clear_WhenNativeCallFails_RetainsRequestForRetry()
    {
        var clearAttempts = 0;
        using var request = new PowerAvailabilityRequest(
            () => true,
            () => true,
            () => ++clearAttempts > 1,
            () => true,
            () => true
        );
        request.Set();

        Assert.Throws<System.ComponentModel.Win32Exception>(() => request.Clear());
        request.Clear();

        Assert.Equal(2, clearAttempts);
    }

    [Fact]
    public void Poke_AfterPartialClearFailure_RestoresBothRequests()
    {
        var clock = new ManualTimeProvider();
        var displaySet = false;
        var systemSet = false;
        var systemClearAttempts = 0;
        using var request = new PowerAvailabilityRequest(
            () => displaySet = true,
            () => systemSet = true,
            () =>
            {
                displaySet = false;
                return true;
            },
            () =>
            {
                systemClearAttempts++;
                if (systemClearAttempts == 1)
                {
                    return false;
                }

                systemSet = false;
                return true;
            },
            () => true
        );
        using var keepAwake = new KeepAwake(
            TimeSpan.FromMilliseconds(50),
            request.Set,
            request.Clear,
            ownedResource: request,
            time: clock,
            isAcquired: () => request.IsFullySet
        );

        keepAwake.Poke();
        clock.Advance(TimeSpan.FromMilliseconds(50));
        clock.WaitForCallbacks();
        Assert.False(keepAwake.IsActive);
        Assert.False(displaySet);
        Assert.True(systemSet);

        keepAwake.Poke();

        Assert.True(displaySet);
        Assert.True(systemSet);
    }

    [Fact]
    public void Clear_AfterDispose_RefusesClosedHandle()
    {
        var request = new PowerAvailabilityRequest(() => true, () => true, () => true, () => true, () => true);
        request.Dispose();

        Assert.Throws<ObjectDisposedException>(() => request.Clear());
    }

    [Fact]
    public void Poke_AfterTerminalRecoveryFailure_StopsReleaseRetries()
    {
        var clock = new ManualTimeProvider();
        var systemSets = 0;
        var displayClears = 0;
        var closes = 0;
        using var request = new PowerAvailabilityRequest(
            () => true,
            () => ++systemSets == 1,
            () => ++displayClears > 2,
            () => true,
            () =>
            {
                closes++;
                return true;
            }
        );
        using var keepAwake = new KeepAwake(
            TimeSpan.FromMilliseconds(50),
            request.Set,
            request.Clear,
            ownedResource: request,
            time: clock,
            isAcquired: () => request.IsFullySet
        );

        keepAwake.Poke();
        clock.Advance(TimeSpan.FromMilliseconds(50));
        clock.WaitForCallbacks();
        keepAwake.Poke();

        Assert.Equal(1, closes);
        Assert.False(keepAwake.IsActive);
        Assert.Equal(0, clock.PendingTimers);
    }

    private sealed class TrackingDisposable : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }
}
