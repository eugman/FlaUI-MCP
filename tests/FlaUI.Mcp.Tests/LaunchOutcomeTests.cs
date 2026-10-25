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
}
