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
