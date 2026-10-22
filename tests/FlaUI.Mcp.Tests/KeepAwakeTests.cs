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
        Assert.Equal(0, released);
        clock.Advance(TimeSpan.FromMilliseconds(1));

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
        Assert.Equal(1, released);

        keepAwake.Poke();

        Assert.Equal(2, acquired);
        Assert.True(keepAwake.IsActive);
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
            Assert.True(released == 0, $"Released too early on iteration {i}.");
        }

        Assert.True(keepAwake.IsActive);
        clock.Advance(TimeSpan.FromMilliseconds(50));
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

    private sealed class TrackingDisposable : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }
}
