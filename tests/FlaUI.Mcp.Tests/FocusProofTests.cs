using System.Text.Json;
using FlaUI.Mcp;
using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// Focus succeeds only when the target window is in front afterwards. Any other foreground change is a change
/// (counted) but not an activation, and windows_focus reports it as a failure instead of ok.
/// </summary>
[Collection(InputLeaseCollection.Name)]
public sealed class FocusProofTests
{
    private const nint Popup = 777;

    private static JsonElement Args(object value) => JsonSerializer.SerializeToElement(value);

    private static async Task<McpToolResult> Focus()
    {
        using var sessions = new SessionManager(null, ProcessPolicy.AllowAll);
        var handle = sessions.PublishWindow(null, FakeDesktop.TargetHwnd, FakeDesktop.TargetPid, FakeDesktop.Target);
        var registry = new ToolRegistry();
        registry.RegisterTool(new FocusWindowTool(sessions, new PendingInvokeTracker()));
        return await registry.ExecuteToolAsync("windows_focus", Args(new { handle }));
    }

    [Fact]
    public async Task FocusThatBringsTheTargetInFrontIsOk()
    {
        using var installed = new FakeDesktop { ForegroundWindow = FakeDesktop.OtherHwnd }.Install();
        var result = await Focus();

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("ok", result.Outcome!.Status);
        Assert.Equal("verified", result.Outcome.Verification);
        Assert.Equal(1, result.Outcome.InputsSent);
    }

    [Fact]
    public async Task FocusThatLeavesAnotherWindowInFrontFails()
    {
        var desktop = new FakeDesktop { ForegroundWindow = FakeDesktop.OtherHwnd, ActivationAllowed = false };
        desktop.ProcessNames[FakeDesktop.OtherPid] = "ForegroundApp";
        using var installed = desktop.Install();
        var result = await Focus();

        Assert.True(result.IsError);
        Assert.Equal("failed", result.Outcome!.Status);
        Assert.Equal("not-dispatched", result.Outcome.Dispatch);
        Assert.Contains("not observed", result.Content[0].Text);
        Assert.Contains("ForegroundApp", result.Content[0].Text);
        Assert.Contains($"PID {FakeDesktop.OtherPid}", result.Content[0].Text);
        Assert.Equal("activation-denied", result.Outcome.Failure?.Code);
        Assert.Equal(FakeDesktop.OtherPid, result.Outcome.Failure?.ForegroundProcess?.Pid);
        Assert.Equal(FakeDesktop.TargetPid, result.Outcome.Failure?.Target?.Pid);
        Assert.False(result.Outcome.Failure?.ActionDispatched);
    }

    [Fact]
    public async Task FocusThatBringsTheWrongWindowInFrontFailsButCountsTheChange()
    {
        using var installed = new FakeDesktop
        {
            ForegroundWindow = FakeDesktop.OtherHwnd,
            FocusLandsOn = Popup,
        }.Install();
        var result = await Focus();

        Assert.True(result.IsError);
        Assert.Equal("partial", result.Outcome!.Dispatch);
        Assert.Equal(1, result.Outcome.InputsSent);
        Assert.Equal("activation-denied", result.Outcome.Failure?.Code);
        Assert.Equal(FakeDesktop.TargetPid, result.Outcome.Failure?.Target?.Pid);
        Assert.True(result.Outcome.Failure?.ActionDispatched);
    }

    [Fact]
    public void GuardedInputDoesNotCallAWrongForegroundWindowAnActivation()
    {
        var desktop = new FakeDesktop { ForegroundWindow = FakeDesktop.OtherHwnd, FocusLandsOn = Popup };
        using var installed = desktop.Install();

        var error = Assert.Throws<InputRefusedException>(() => new GuardedInput(FakeDesktop.Target));

        Assert.Equal("activation-denied", error.Refusal.Code);
        Assert.DoesNotContain("target window was activated", error.Message);
        Assert.Contains("the foreground window changed", error.Message);
    }
}
