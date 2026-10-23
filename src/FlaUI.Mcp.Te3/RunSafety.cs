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

    // Direct process operations are desktop mutations too. The runner grant can be revoked while
    // a recipe is active, including just before cleanup in the finally block.
    internal static void GuardedProcessMutation(Action mutation) => MutationGuard.Execute(static () => { }, mutation);

    public static async Task CloseOwned(int pid, long startedTicks, string executable, string? journal = null)
    {
        if (pid == 0)
        {
            return;
        }

        Process process;
        try
        {
            process = Process.GetProcessById(pid);
        }
        catch (ArgumentException)
        {
            return;
        }

        using (process)
        {
            if (!VerifyUnlessExited(process, () => VerifyIdentity(process, startedTicks, executable)))
            {
                return;
            }

            await RunJournal.Input(
                journal,
                "te3_close_main_window",
                new { pid },
                () => GuardedProcessMutation(() => process.CloseMainWindow())
            );
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                if (!VerifyUnlessExited(process, () => VerifyIdentity(process, startedTicks, executable)))
                {
                    return;
                }

                GuardedProcessMutation(() => process.Kill());
                using var killed = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await process.WaitForExitAsync(killed.Token);
            }
        }
    }
}
