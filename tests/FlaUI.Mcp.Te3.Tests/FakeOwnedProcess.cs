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

    public Task RequestClose()
    {
        Calls.Add("close");
        Exit(ExitsOnClose);
        return Task.CompletedTask;
    }

    public void Kill()
    {
        Calls.Add("kill");
        Exit(true);
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
