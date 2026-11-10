using System.Diagnostics;
using FlaUI.Mcp.Core;

public static class RunSafety
{
    /// <param name="running">The TE3 process IDs; the real process list when null.</param>
    public static void RequireExclusiveTe3(int? ownedPid = null, Func<int[]>? running = null)
    {
        var ids = (running ?? RunningTe3)().Where(id => id != ownedPid).ToArray();
        if (ids.Length != 0)
        {
            throw new InvalidOperationException(
                $"Close TE3 yourself before running/recovery. Existing process IDs: {string.Join(", ", ids)}. No user window will be closed."
            );
        }
    }

    private static int[] RunningTe3()
    {
        var processes = Process.GetProcessesByName("TabularEditor3");
        try
        {
            return processes.Where(p => !p.HasExited).Select(p => p.Id).ToArray();
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    public static void VerifyIdentity(Process process, long startedTicks, string executable)
    {
        if (
            startedTicks == 0
            || process.StartTime.ToUniversalTime().Ticks != startedTicks
            || !string.Equals(
                process.MainModule?.FileName,
                Path.GetFullPath(executable),
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            throw new InvalidOperationException("Process identity mismatch; refusing termination.");
        }
    }

    // An exiting process can fail StartTime/MainModule reads. Exit is the goal, not an identity mismatch.
    internal static bool VerifyUnlessExited(Process process, Action verifyIdentity)
    {
        if (process.HasExited)
        {
            return false;
        }

        try
        {
            verifyIdentity();
            return true;
        }
        catch (Exception) when (process.HasExited)
        {
            return false;
        }
    }

    /// <summary>How long the run's TE3 gets to exit after a close request, and again after a kill.</summary>
    private static readonly TimeSpan ExitGrace = TimeSpan.FromSeconds(5);

    /// <summary>Closes the TE3 this run launched, identified by PID, start time and executable.</summary>
    /// <returns>True when it did not exit on its close request and was killed.</returns>
    public static async Task<bool> CloseOwned(int pid, long startedTicks, string executable, string? journal = null)
    {
        if (pid == 0)
        {
            return false;
        }

        Process process;
        try
        {
            process = Process.GetProcessById(pid);
        }
        catch (ArgumentException)
        {
            return false;
        }

        using (process)
        {
            return await CloseOwned(new LiveProcess(process, startedTicks, executable, journal), ExitGrace);
        }
    }

    /// <summary>
    /// The close and, failing that, the kill of the process the run launched. Neither asks the run's grant: the
    /// user's input revokes the grant, and a run that then left its TE3 open could not restore the user's
    /// preferences. Each is sent only straight after the process is verified to be the one the run started (PID,
    /// start time, executable), and neither takes focus. Every other input still checks the grant.
    /// </summary>
    internal static async Task<bool> CloseOwned(IOwnedProcess process, TimeSpan grace)
    {
        if (!process.VerifyUnlessExited())
        {
            return false;
        }

        await process.RequestClose();
        using var timeout = new CancellationTokenSource(grace);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            return false;
        }
        catch (OperationCanceledException)
        {
            if (!process.VerifyUnlessExited())
            {
                return false;
            }

            process.Kill();
            using var killed = new CancellationTokenSource(grace);
            await process.WaitForExitAsync(killed.Token);
            return true;
        }
    }

    /// <summary>The process a run launched, as its cleanup needs it; tests script one.</summary>
    internal interface IOwnedProcess
    {
        /// <summary>False when it has exited; throws when a live process is not the one the run launched.</summary>
        bool VerifyUnlessExited();

        /// <summary>Asks the main window to close, journaled like a tool call.</summary>
        Task RequestClose();

        void Kill();

        Task WaitForExitAsync(CancellationToken cancellationToken);
    }

    private sealed class LiveProcess(Process process, long startedTicks, string executable, string? journal)
        : IOwnedProcess
    {
        public bool VerifyUnlessExited() =>
            RunSafety.VerifyUnlessExited(process, () => VerifyIdentity(process, startedTicks, executable));

        public Task RequestClose() =>
            RunJournal.Input(
                journal,
                "te3_close_main_window",
                new { pid = process.Id },
                () => process.CloseMainWindow()
            );

        public void Kill() => process.Kill();

        public Task WaitForExitAsync(CancellationToken cancellationToken) =>
            process.WaitForExitAsync(cancellationToken);
    }
}
