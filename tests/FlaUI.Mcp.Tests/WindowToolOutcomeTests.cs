using System.Text.Json;
using FlaUI.Mcp;
using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>Focus and close by UI Automation report a blocked or still-running provider as such.</summary>
[Collection(InputLeaseCollection.Name)]
public sealed class WindowToolOutcomeTests
{
    private static JsonElement Args(object value) => JsonSerializer.SerializeToElement(value);

    /// <summary>A window known only through UI Automation (no native handle) whose provider is blocked.</summary>
    private static (SessionManager Sessions, string Handle, PendingInvokeTracker Pending) BlockedUiaWindow()
    {
        var sessions = new SessionManager(null, ProcessPolicy.AllowAll);
        var handle = sessions.PublishWindow(null, 0, FakeDesktop.TargetPid, null);
        var pending = new PendingInvokeTracker(_ => 1);
        pending.Begin(FakeDesktop.TargetPid, "Invoke on 'Open...'");
        return (sessions, handle, pending);
    }

    [Fact]
    public async Task FocusOfABlockedWindowIsABlockedResult()
    {
        using var offlineDesktop = new FakeDesktop().Install();
        var (sessions, handle, pending) = BlockedUiaWindow();
        using var _ = sessions;

        var result = await new FocusWindowTool(sessions, pending).ExecuteAsync(Args(new { handle }));

        Assert.True(result.IsError);
        Assert.Equal("not-dispatched", result.Outcome!.Dispatch);
        Assert.Equal("pending", result.Outcome.Provider);
    }

    [Fact]
    public async Task CloseOfABlockedWindowIsABlockedResult()
    {
        using var offlineDesktop = new FakeDesktop().Install();
        var (sessions, handle, pending) = BlockedUiaWindow();
        using var _ = sessions;

        var result = await new CloseWindowTool(sessions, pending).ExecuteAsync(Args(new { handle }));

        Assert.True(result.IsError);
        Assert.Equal("not-dispatched", result.Outcome!.Dispatch);
        Assert.Equal("pending", result.Outcome.Provider);
    }

    [Fact]
    public void AUiaMutationStillRunningReturnsItsPendingResult()
    {
        using var offlineDesktop = new FakeDesktop().Install();
        using var hold = new ManualResetEventSlim();
        try
        {
            var result = WindowMutation.Uia(0, "Close on w1", hold.Wait, new PendingInvokeTracker());

            Assert.Equal(PatternCallOutcome.StillPending, result.Outcome);
        }
        finally
        {
            hold.Set();
        }
    }
}
