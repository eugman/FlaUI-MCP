using System.Diagnostics;

/// <summary>Operator-controlled pause; never sends UI input while an agent owns the desktop.</summary>
public static class AgentHandoff
{
    public static async Task Wait(Func<string?> readRelease, TimeSpan timeout)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < timeout)
        {
            var release = readRelease();
            if (release == "grade") return;
            if (release == "abort") throw new InvalidOperationException("Agent trial aborted by operator");
            if (release != null) throw new InvalidOperationException("Unknown handoff release; expected grade or abort");
            await Task.Delay(100);
        }
        throw new TimeoutException("Agent handoff expired; trial is not a pass");
    }
}
