using System.Collections.Concurrent;
using System.Diagnostics;

namespace FlaUI.Mcp.Core;

/// <summary>
/// Physical input is global, so its lease is machine-wide: a named mutex every FlaUI-MCP host on the machine shares
/// (<see cref="DefaultName"/>), taken after an in-process gate. Two hosts never interleave input, a recording host
/// blocks a second one, and a host that exits while holding it abandons the mutex, which the next host takes over.
/// The mutex is owned by one dedicated thread, so a lease may be released from any thread. Offline tests set
/// <see cref="NameVariable"/> to a name of their own process, so they neither wait on nor block a real host.
/// </summary>
internal static class DesktopLease
{
    public const string DefaultName = @"Global\FlaUI-MCP-DesktopInput";

    /// <summary>Overrides the mutex name; only the offline test module initializer sets it.</summary>
    public const string NameVariable = "FLAUI_LEASE_NAME";

    private static readonly SemaphoreSlim InProcess = new(1, 1);
    private static readonly Lazy<MutexOwner> Owner = new(() => new MutexOwner(Name));
    private static int held;

    public static string Name =>
        Environment.GetEnvironmentVariable(NameVariable) is { Length: > 0 } name ? name : DefaultName;

    /// <summary>Whether this process holds the lease now.</summary>
    public static bool Held => Volatile.Read(ref held) != 0;

    public static IDisposable Acquire(TimeSpan timeout, CancellationToken token = default)
    {
        var clock = Stopwatch.StartNew();
        if (!InProcess.Wait(timeout, token))
        {
            throw new TimeoutException("Desktop input is busy; no input sent.");
        }

        try
        {
            var remaining = timeout - clock.Elapsed;
            if (!Owner.Value.Acquire(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero, token))
            {
                token.ThrowIfCancellationRequested();
                throw new TimeoutException(
                    "Desktop input is busy: another FlaUI-MCP host holds the desktop lease; no input sent."
                );
            }
        }
        catch
        {
            InProcess.Release();
            throw;
        }

        Volatile.Write(ref held, 1);
        return new Lease();
    }

    private sealed class Lease : IDisposable
    {
        private int released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref released, 1) != 0)
            {
                return;
            }

            Volatile.Write(ref held, 0);
            Owner.Value.Release();
            InProcess.Release();
        }
    }

    /// <summary>The thread that owns the named mutex, since a mutex must be released by the thread that took it.</summary>
    private sealed class MutexOwner
    {
        private readonly BlockingCollection<Action> work = new();

        public MutexOwner(string name)
        {
            var thread = new Thread(() => Run(name)) { IsBackground = true, Name = "FlaUI-MCP desktop lease" };
            thread.Start();
        }

        private void Run(string name)
        {
            using var mutex = new Mutex(false, name);
            Current = mutex;
            foreach (var step in work.GetConsumingEnumerable())
            {
                step();
            }
        }

        private Mutex? Current { get; set; }

        public bool Acquire(TimeSpan timeout, CancellationToken token)
        {
            var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            work.Add(() => result.SetResult(Take(timeout, token)));
            return result.Task.GetAwaiter().GetResult();
        }

        public void Release()
        {
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            work.Add(() =>
            {
                Current!.ReleaseMutex();
                done.SetResult();
            });
            done.Task.GetAwaiter().GetResult();
        }

        private bool Take(TimeSpan timeout, CancellationToken token)
        {
            try
            {
                var signalled = WaitHandle.WaitAny([Current!, token.WaitHandle], timeout);
                if (signalled == 1)
                {
                    return false;
                }

                return signalled == 0;
            }
            catch (AbandonedMutexException)
            {
                // The host that held it exited without releasing it; this thread owns it now.
                return true;
            }
        }
    }
}
