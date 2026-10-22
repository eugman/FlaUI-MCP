using System.Collections.Concurrent;
using System.Diagnostics;

namespace FlaUI.Mcp.Core;

/// <summary>
/// Physical input is global, so its lease is machine-wide: a named mutex every FlaUI-MCP host on the machine shares
/// (<see cref="DefaultName"/>), taken after an in-process gate. Two hosts never interleave input, a recording host
/// blocks a second one, and a host that exits while holding it abandons the mutex, which the next host takes over.
/// The mutex is owned by one dedicated thread, so a lease may be released from any thread. Offline tests set
/// <see cref="NameVariable"/> to a name of their own process, so they neither wait on nor block a real host; the
/// override counts only with FLAUI_OFFLINE_TESTS=1.
/// </summary>
internal static class DesktopLease
{
    public const string DefaultName = @"Global\FlaUI-MCP-DesktopInput";

    /// <summary>Overrides the mutex name; only the offline test module initializer sets it.</summary>
    public const string NameVariable = "FLAUI_LEASE_NAME";

    private static readonly SemaphoreSlim InProcess = new(1, 1);
    private static readonly Lazy<MutexOwner> Owner = new(() => new MutexOwner(Name));
    private static int held;

    /// <summary>The mutex name: the override only in an offline test process, so no host can be pointed elsewhere.</summary>
    public static string Name =>
        NameFor(
            Environment.GetEnvironmentVariable(InputDesktop.OfflineVariable),
            Environment.GetEnvironmentVariable(NameVariable)
        );

    internal static string NameFor(string? offline, string? name) =>
        offline == "1" && !string.IsNullOrEmpty(name) ? name : DefaultName;

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
    internal sealed class MutexOwner
    {
        private readonly BlockingCollection<Action> work = new();
        private Mutex? mutex;
        private Exception? openFailure;

        public MutexOwner(string name)
            : this(() => new Mutex(false, name)) { }

        /// <param name="open">Opens the named mutex; tests pass one that fails.</param>
        internal MutexOwner(Func<Mutex> open)
        {
            var thread = new Thread(() => Run(open)) { IsBackground = true, Name = "FlaUI-MCP desktop lease" };
            thread.Start();
        }

        // Every exception on this thread goes back to the caller: a failure here must refuse input, never end the host.
        private void Run(Func<Mutex> open)
        {
            try
            {
                mutex = open();
            }
            catch (Exception error)
            {
                openFailure = error;
            }

            foreach (var step in work.GetConsumingEnumerable())
            {
                step();
            }
        }

        public bool Acquire(TimeSpan timeout, CancellationToken token) =>
            Post(() =>
            {
                if (openFailure != null)
                {
                    throw Unavailable(openFailure);
                }

                return Take(timeout, token);
            });

        public void Release() =>
            Post(() =>
            {
                mutex?.ReleaseMutex();
                return true;
            });

        /// <summary>Run a step on the owner thread and return its result, or rethrow what it threw.</summary>
        private bool Post(Func<bool> step)
        {
            var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            work.Add(() =>
            {
                try
                {
                    result.SetResult(step());
                }
                catch (Exception error)
                {
                    result.SetException(error);
                }
            });
            try
            {
                return result.Task.GetAwaiter().GetResult();
            }
            catch (Exception error) when (error is not (AutomationRefusal or OperationCanceledException))
            {
                throw Unavailable(error);
            }
        }

        private bool Take(TimeSpan timeout, CancellationToken token)
        {
            try
            {
                var signalled = WaitHandle.WaitAny([mutex!, token.WaitHandle], timeout);
                return signalled == 0;
            }
            catch (AbandonedMutexException)
            {
                // The host that held it exited without releasing it; this thread owns it now.
                return true;
            }
        }

        private static InputRefusedException Unavailable(Exception error) =>
            InputRefusedException.Record(
                new InputRefusal(
                    "lease-unavailable",
                    $"The machine-wide desktop lease could not be used ({error.Message}); no input sent."
                )
                {
                    ActionDispatched = false,
                }
            );
    }
}
