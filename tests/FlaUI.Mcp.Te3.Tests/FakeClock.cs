namespace FlaUI.Mcp.Te3.Tests;

/// <summary>Time that passes only when the code under test waits: a wait moves it on, and nothing really sleeps.</summary>
internal sealed class FakeClock
{
    public TimeSpan Now { get; private set; }

    public TimeSpan Elapsed() => Now;

    public Task Delay(int ms)
    {
        Now += TimeSpan.FromMilliseconds(ms);
        return Task.CompletedTask;
    }
}
