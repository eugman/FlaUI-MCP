namespace FlaUI.Mcp.Te3.Tests;

/// <summary>A run's TE3, scripted: given a preferences path it rewrites that file when it exits, as the real one does.</summary>
internal sealed class FakeOwnedProcess(string? preferences = null) : RunSafety.IOwnedProcess
{
    public bool Exited { get; private set; }
    public bool ExitsOnClose { get; init; } = true;
    public bool Mismatch { get; init; }
    public List<string> Calls { get; } = [];

    public bool VerifyUnlessExited()
    {
        if (Exited)
        {
            return false;
        }

        Calls.Add("verify");
        if (Mismatch)
        {
            throw new InvalidOperationException("Process identity mismatch; refusing termination.");
        }

        return true;
    }

    /// <summary>False for a process with no main window to post a close message to.</summary>
    public bool Posts { get; init; } = true;

    /// <summary>Why it was killed, as the journal would record it.</summary>
    public string? KillReason { get; private set; }

    public Task<bool> RequestClose()
    {
        Calls.Add("close");
        Exit(Posts && ExitsOnClose);
        return Task.FromResult(Posts);
    }

    public Task Kill(string reason)
    {
        Calls.Add("kill");
        KillReason = reason;
        Exit(true);
        return Task.CompletedTask;
    }

    private void Exit(bool exits)
    {
        if (!exits)
        {
            return;
        }

        Exited = true;
        if (preferences != null)
        {
            File.WriteAllText(preferences, """{"Skin":"written by TE3 on exit"}""");
        }
    }

    public Task WaitForExitAsync(CancellationToken cancellationToken) =>
        Exited ? Task.CompletedTask : Task.Delay(Timeout.Infinite, cancellationToken);
}
