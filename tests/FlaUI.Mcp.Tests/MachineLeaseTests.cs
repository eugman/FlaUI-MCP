using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// The input lease is machine-wide: a second host (another process) holding it makes this one refuse, so two
/// hosts never interleave input during a recording. The offline tests use a lease named for their own process.
/// </summary>
[Collection(InputLeaseCollection.Name)]
public sealed class MachineLeaseTests
{
    [Fact]
    public void TheOfflineTestsUseALeaseNamedForTheirOwnProcess() =>
        Assert.Equal($@"Local\FlaUI-MCP-Tests-{Environment.ProcessId}", DesktopLease.Name);

    [Fact]
    public void ALeaseHeldByAnotherProcessRefusesInput()
    {
        using var otherHostHolds = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        // Another thread opening the named mutex stands in for another process: the mutex is what both share.
        var other = new Thread(() =>
        {
            using var mutex = new Mutex(false, DesktopLease.Name);
            mutex.WaitOne();
            otherHostHolds.Set();
            release.Wait();
            mutex.ReleaseMutex();
        });
        other.Start();
        otherHostHolds.Wait();
        try
        {
            var error = Assert.Throws<TimeoutException>(() => DesktopLease.Acquire(TimeSpan.FromMilliseconds(200)));
            Assert.Contains("another FlaUI-MCP host holds the desktop", error.Message);
            Assert.False(DesktopLease.Held);
        }
        finally
        {
            release.Set();
            other.Join();
        }

        using var lease = DesktopLease.Acquire(TimeSpan.FromSeconds(2));
        Assert.True(DesktopLease.Held);
    }

    [Fact]
    public void ALeaseLeftByAProcessThatDiedIsTakenOver()
    {
        // A host that exits while holding the lease abandons the mutex; the next host takes it.
        var other = new Thread(() =>
        {
            var mutex = new Mutex(false, DesktopLease.Name);
            mutex.WaitOne();
        });
        other.Start();
        other.Join();

        using var lease = DesktopLease.Acquire(TimeSpan.FromSeconds(2));
        Assert.True(DesktopLease.Held);
    }

    [Fact]
    public void ALeaseIsReleasedFromAnyThread()
    {
        var lease = DesktopLease.Acquire(TimeSpan.FromSeconds(2));
        Task.Run(lease.Dispose).GetAwaiter().GetResult();

        using var again = DesktopLease.Acquire(TimeSpan.FromMilliseconds(200));
    }
}

/// <summary>The lease name override is for offline tests only, and a lease that cannot be opened is a refusal.</summary>
[Collection(InputLeaseCollection.Name)]
public sealed class MachineLeaseFailureTests
{
    [Theory]
    [InlineData("1", @"Local\x", @"Local\x")]
    [InlineData(null, @"Local\x", DesktopLease.DefaultName)]
    [InlineData("0", @"Local\x", DesktopLease.DefaultName)]
    [InlineData("1", null, DesktopLease.DefaultName)]
    public void TheOverrideCountsOnlyInOfflineTests(string? offline, string? name, string expected) =>
        Assert.Equal(expected, DesktopLease.NameFor(offline, name));

    [Fact]
    public void AMutexThatCannotBeOpenedIsALeaseRefusalNotACrash()
    {
        var owner = new DesktopLease.MutexOwner(() =>
            throw new UnauthorizedAccessException("Access to the path is denied.")
        );

        var error = Assert.Throws<InputRefusedException>(() => owner.Acquire(TimeSpan.FromMilliseconds(100), default));

        Assert.Equal("lease-unavailable", error.Refusal.Code);
        Assert.Contains("Access to the path is denied", error.Message);
        Assert.Contains("no input sent", error.Message);
    }
}

/// <summary>A lease handle that stops working (disposed under the owner) is also a refusal, not a crash.</summary>
[Collection(InputLeaseCollection.Name)]
public sealed class MachineLeaseDisposedTests
{
    [Fact]
    public void ADisposedMutexHandleIsALeaseRefusal()
    {
        var owner = new DesktopLease.MutexOwner(() =>
        {
            var mutex = new Mutex(false, $@"Local\FlaUI-MCP-Disposed-{Guid.NewGuid():N}");
            mutex.Dispose();
            return mutex;
        });

        var error = Assert.Throws<InputRefusedException>(() => owner.Acquire(TimeSpan.FromMilliseconds(100), default));

        Assert.Equal("lease-unavailable", error.Refusal.Code);
    }
}
