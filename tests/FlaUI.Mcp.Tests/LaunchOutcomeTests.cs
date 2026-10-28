using System.Text.Json;
using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

public sealed class LaunchOutcomeTests
{
    [Fact]
    public void StableSplashHandleIsProvisionalNotACompletedLaunch()
    {
        var observed = new Win32WindowInfo(101, "Splash", 7321, true, false, false);

        var result = LaunchTool.ProvisionalResult("demo.exe", "w1", observed.ProcessId, observed.Title);

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("unverified", result.Outcome?.Status);
        Assert.Equal("launch-provisional", result.Outcome?.Failure?.Code);
        Assert.Contains("provisional", result.Content[0].Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expected controls", result.Content[0].Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OneSecondLaunchBudgetCanReturnAProvisionalWindow()
    {
        nint previous = 0;
        var consecutive = 0;
        var polls = SessionManager.LaunchPollCount(1);
        var settlePolls = SessionManager.LaunchSettlePolls(polls);
        var window = new Win32WindowInfo(7, "Main", 1, true, false, false);

        Assert.Equal(2, polls);
        Assert.Equal(2, settlePolls);
        Assert.Null(SessionManager.ObserveLaunchWindow(ref previous, ref consecutive, [window], settlePolls));
        Assert.Equal((nint)7, SessionManager.ObserveLaunchWindow(ref previous, ref consecutive, [window], settlePolls));
    }

    [Fact]
    public void StartedProcessWithoutUniqueWindowHasPendingIdentity()
    {
        Win32WindowInfo[] observed =
        [
            new(101, "Splash", 7321, true, false, false),
            new(102, "Main", 7321, true, false, false),
        ];
        var pending = new LaunchPendingException("demo.exe", 7321, observed, false);

        var result = LaunchTool.PendingResult(pending);

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("unverified", result.Outcome?.Status);
        Assert.Equal("launch-pending", result.Outcome?.Failure?.Code);
        Assert.True(result.Outcome?.Failure?.ActionDispatched);
        Assert.Equal(7321, result.Outcome?.Details?["processId"].GetInt32());
        var windows = result.Outcome?.Details?["observedWindows"].EnumerateArray().ToArray();
        Assert.Equal(2, windows?.Length);
        Assert.Equal("Splash", windows?[0].GetProperty("title").GetString());
        Assert.Equal(101, windows?[0].GetProperty("hwnd").GetInt64());
    }

    [Fact]
    public void ExitedLauncherIsUnverifiedHandoffNotProvenFailureOrSuccess()
    {
        var result = LaunchTool.PendingResult(new LaunchPendingException("demo.exe", 7321, [], false, handoff: true));

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("unverified", result.Outcome?.Status);
        Assert.Equal("partial", result.Outcome?.Dispatch);
        Assert.Equal("launch-handoff", result.Outcome?.Failure?.Code);
        Assert.True(result.Outcome?.Failure?.ActionDispatched);
        Assert.Equal(7321, result.Outcome?.Details?["processId"].GetInt32());
        Assert.Contains("Do not blindly relaunch", result.Content[0].Text);
        Assert.DoesNotContain("Started demo.exe", result.Content[0].Text);
    }

    [Fact]
    public void NullProcessHandleIsUnverifiedHandoffWithoutAnInventedPid()
    {
        var result = LaunchTool.PendingResult(new LaunchPendingException("demo.exe", 0, [], false, handoff: true));

        Assert.Equal("launch-handoff", result.Outcome?.Failure?.Code);
        Assert.False(result.Outcome?.Details?.ContainsKey("processId"));
        Assert.Contains("no process handle", result.Content[0].Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PID 0", result.Content[0].Text);
    }

    [Fact]
    public void LongLaunchWaitHasItsOwnCallerDeadlineWithoutChangingOtherTools()
    {
        using var installed = new FakeDesktop().Install();
        using var sessions = new SessionManager(null, ProcessPolicy.AllowAll);
        var tool = new LaunchTool(sessions);

        Assert.Equal(TimeSpan.FromSeconds(35), tool.TimeoutForArguments(null));
        Assert.Equal(
            TimeSpan.FromSeconds(60),
            tool.TimeoutForArguments(JsonSerializer.SerializeToElement(new { waitSeconds = 45 }))
        );
        Assert.Null(tool.TimeoutForArguments(JsonSerializer.SerializeToElement(new { waitSeconds = 91 })));
        Assert.Null(tool.TimeoutForArguments(JsonSerializer.SerializeToElement(new { waitSeconds = "slow" })));
    }

    [Fact]
    public async Task WrongWaitTypeIsRejectedBeforeLaunchWithStructuredZeroDispatch()
    {
        using var installed = new FakeDesktop().Install();
        using var sessions = new SessionManager(null, ProcessPolicy.AllowAll);
        var registry = new ToolRegistry();
        registry.RegisterTool(new LaunchTool(sessions));

        var result = await registry.ExecuteToolAsync(
            "windows_launch",
            JsonSerializer.SerializeToElement(new { app = "demo.exe", waitSeconds = "slow" })
        );

        Assert.Equal("not-dispatched", result.Outcome?.Dispatch);
        Assert.Equal(0, result.Outcome?.InputsSent);
    }

    [Fact]
    public async Task MissingExecutableIsTypedFailureWithZeroDispatch()
    {
        using var installed = new FakeDesktop { StartFails = true }.Install();
        using var sessions = new SessionManager(null, ProcessPolicy.AllowAll);
        var registry = new ToolRegistry();
        registry.RegisterTool(new LaunchTool(sessions));

        var result = await registry.ExecuteToolAsync(
            "windows_launch",
            JsonSerializer.SerializeToElement(new { app = "missing.exe" })
        );

        Assert.Equal("launch-failed", result.Outcome?.Failure?.Code);
        Assert.Equal("not-dispatched", result.Outcome?.Dispatch);
        Assert.Equal(0, result.Outcome?.InputsSent);
    }

    [Fact]
    public async Task NullShellStartIsUnverifiedHandoffWithOnePossibleDispatch()
    {
        using var installed = new FakeDesktop { StartsNothing = true }.Install();
        using var sessions = new SessionManager(null, ProcessPolicy.AllowAll);
        var registry = new ToolRegistry();
        registry.RegisterTool(new LaunchTool(sessions));

        var result = await registry.ExecuteToolAsync(
            "windows_launch",
            JsonSerializer.SerializeToElement(new { app = "demo.exe" })
        );

        Assert.Equal("launch-handoff", result.Outcome?.Failure?.Code);
        Assert.Equal("partial", result.Outcome?.Dispatch);
        Assert.Equal("unverified", result.Outcome?.Status);
        Assert.Equal(1, result.Outcome?.InputsSent);
        Assert.False(result.Outcome?.Details?.ContainsKey("processId"));
    }
}
