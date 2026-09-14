using System.Diagnostics;
public static class RunSafety
{
    public static void RequireExclusiveTe3(int? ownedPid = null)
    {
        var processes = Process.GetProcessesByName("TabularEditor3");
        try
        {
            var ids = processes.Where(p => p.Id != ownedPid && !p.HasExited).Select(p => p.Id).ToArray();
            if (ids.Length != 0) throw new InvalidOperationException($"Close TE3 yourself before running/recovery. Existing process IDs: {string.Join(", ", ids)}. No user window will be closed.");
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }
    public static void VerifyIdentity(Process process, long startedTicks, string executable)
    {
        if (startedTicks == 0 || process.StartTime.ToUniversalTime().Ticks != startedTicks ||
            !string.Equals(process.MainModule?.FileName, Path.GetFullPath(executable), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Process identity mismatch; refusing termination.");
    }
    // An exiting process can fail StartTime/MainModule reads. Exit is the goal, not an identity mismatch.
    internal static bool VerifyUnlessExited(Process process, Action verifyIdentity)
    {
        if (process.HasExited) return false;
        try { verifyIdentity(); return true; }
        catch (Exception) when (process.HasExited) { return false; }
    }
    public static async Task CloseOwned(int pid, long startedTicks, string executable)
    {
        if (pid == 0) return;
        Process process;
        try { process = Process.GetProcessById(pid); } catch (ArgumentException) { return; }
        using (process)
        {
            if (!VerifyUnlessExited(process, () => VerifyIdentity(process, startedTicks, executable))) return;
            process.CloseMainWindow();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                if (!VerifyUnlessExited(process, () => VerifyIdentity(process, startedTicks, executable))) return;
                process.Kill();
                using var killed = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await process.WaitForExitAsync(killed.Token);
            }
        }
    }
}
