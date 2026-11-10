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

    /// <summary>How the run's own TE3 ended.</summary>
    public enum OwnedExit
    {
        /// <summary>It had already exited, or was never started.</summary>
        NotRunning,

        /// <summary>It exited on its close request.</summary>
        Closed,

        /// <summary>Its close request was not posted or not obeyed, and it was killed.</summary>
        KilledAfterClose,

        /// <summary>It was killed without a close request (the run's grant was revoked).</summary>
        KilledUnasked,
    }

    /// <summary>Ends the TE3 this run launched, identified by PID, start time and executable.</summary>
    /// <param name="askFirst">False once the run's grant is revoked: see the overload below.</param>
    public static async Task<OwnedExit> CloseOwned(
        int pid,
        long startedTicks,
        string executable,
        string? journal = null,
        bool askFirst = true
    )
    {
        if (pid == 0)
        {
            return OwnedExit.NotRunning;
        }

        Process process;
        try
        {
            process = Process.GetProcessById(pid);
        }
        catch (ArgumentException)
        {
            return OwnedExit.NotRunning;
        }

        using (process)
        {
            return await CloseOwned(new LiveProcess(process, startedTicks, executable, journal), ExitGrace, askFirst);
        }
    }

    internal const string NotObeyed = "it did not exit after its close request";
    internal const string NotPosted = "no close message could be posted to it";
    internal const string Revoked =
        "the run's grant was revoked, and a close request could raise a save prompt on the user's desktop";

    /// <summary>
    /// The end of the process the run launched. With <paramref name="askFirst"/> its main window is asked to close
    /// and it is killed only when that is not posted or not obeyed. Without it (the user's input revoked the run's
    /// grant) there is no close request, because TE3 answers one on a model with unsaved edits with a save prompt,
    /// on the desktop the user has just taken back: it is killed at once; the model is the run's disposable one.
    /// Neither the close nor the kill asks the grant, or a revoked run could not restore the user's preferences;
    /// each is sent only straight after the process is verified to be the one the run started (PID, start time,
    /// executable), and neither takes focus. Every other input still checks the grant.
    /// </summary>
    internal static async Task<OwnedExit> CloseOwned(IOwnedProcess process, TimeSpan grace, bool askFirst)
    {
        if (!process.VerifyUnlessExited())
        {
            return OwnedExit.NotRunning;
        }

        var reason = Revoked;
        if (askFirst)
        {
            var posted = await process.RequestClose();
            if (posted && await Exited(process, grace))
            {
                return OwnedExit.Closed;
            }

            reason = posted ? NotObeyed : NotPosted;
        }

        if (!process.VerifyUnlessExited())
        {
            return OwnedExit.Closed;
        }

        await process.Kill(reason);
        if (!await Exited(process, grace))
        {
            throw new TimeoutException("The run's TE3 did not exit after it was killed.");
        }

        return askFirst ? OwnedExit.KilledAfterClose : OwnedExit.KilledUnasked;
    }

    private static async Task<bool> Exited(IOwnedProcess process, TimeSpan grace)
    {
        using var timeout = new CancellationTokenSource(grace);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>The process a run launched, as its cleanup needs it; tests script one.</summary>
    internal interface IOwnedProcess
    {
        /// <summary>False when it has exited; throws when a live process is not the one the run launched.</summary>
        bool VerifyUnlessExited();

        /// <summary>Asks the main window to close, journaled with whether the message was posted.</summary>
        /// <returns>False when no close message was posted (no main window, or it is not responding).</returns>
        Task<bool> RequestClose();

        /// <summary>Kills the process, journaled with <paramref name="reason"/>.</summary>
        Task Kill(string reason);

        Task WaitForExitAsync(CancellationToken cancellationToken);
    }

    private sealed class LiveProcess(Process process, long startedTicks, string executable, string? journal)
        : IOwnedProcess
    {
        public bool VerifyUnlessExited() =>
            RunSafety.VerifyUnlessExited(process, () => VerifyIdentity(process, startedTicks, executable));

        public async Task<bool> RequestClose()
        {
            var posted = false;
            await RunJournal.Said(
                journal,
                "te3_close_main_window",
                new { pid = process.Id },
                () => (posted = process.CloseMainWindow()) ? "Close message posted." : "No close message was posted."
            );
            return posted;
        }

        public Task Kill(string reason) =>
            RunJournal.Said(
                journal,
                "te3_kill",
                new { pid = process.Id, reason },
                () =>
                {
                    process.Kill();
                    return "Process killed.";
                }
            );

        public Task WaitForExitAsync(CancellationToken cancellationToken) =>
            process.WaitForExitAsync(cancellationToken);
    }
}
