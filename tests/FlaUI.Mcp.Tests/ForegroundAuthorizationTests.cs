using System.Diagnostics;
using PlaywrightWindows.Mcp.Core;
using PlaywrightWindows.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

public sealed class ForegroundAuthorizationTests
{
    [Fact]
    public void InputAuthorizesTheCapturedProcessBeforeReturningItsTarget()
    {
        using var process = Process.GetCurrentProcess();
        var policy = new ProcessPolicy([process.ProcessName]);
        var captured = new InputTarget(123, -1, 0);
        var captures = 0;

        var error = Assert.Throws<InvalidOperationException>(() => GuardedInput.ForegroundTarget(policy, () =>
        {
            captures++;
            return captured; // Focus changed from the allowed process before capture.
        }));

        Assert.Equal(1, captures);
        Assert.Contains("not in the FlaUI-MCP app allowlist", error.Message);
        Assert.Contains("Focus an allowed window first", error.Message);
    }

    [Fact]
    public void AllowedInputPreservesCapturedWindowAndPopupIdentity()
    {
        using var process = Process.GetCurrentProcess();
        var captured = new InputTarget(123, process.Id, process.StartTime.ToUniversalTime().Ticks, 456);

        var result = GuardedInput.ForegroundTarget(new ProcessPolicy([process.ProcessName]), () => captured);

        Assert.Same(captured, result);
    }

    [Fact]
    public void ActivationFailureDistinguishesRefusedActivationFromMissingDesktop()
    {
        Assert.Null(GuardedInput.DescribeActivationFailure(42, 100, 42, "TabularEditor3"));

        var denied = GuardedInput.DescribeActivationFailure(42, 100, 7, "WindowsTerminal")!;
        Assert.StartsWith("activation-denied:", denied);
        Assert.Contains("'WindowsTerminal' (PID 7)", denied);
        Assert.Contains("title bar", denied);

        Assert.StartsWith("desktop-unavailable:", GuardedInput.DescribeActivationFailure(42, 0, 0, null));
    }

    [Fact]
    public void ScreenshotDeniesSelectedProcessBeforeReadingPixels()
    {
        using var process = Process.GetCurrentProcess();
        var capturedPixels = false;

        var error = Assert.Throws<UnauthorizedAccessException>(() => ScreenshotTool.CaptureForeground(
            -1, new ProcessPolicy([process.ProcessName]), () => capturedPixels = true));

        Assert.False(capturedPixels);
        Assert.Contains("The foreground window's process 'unknown'", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScreenshotCapturesAllowedSelectedProcessOnce(bool restricted)
    {
        using var process = Process.GetCurrentProcess();
        var policy = restricted ? new ProcessPolicy([process.ProcessName]) : ProcessPolicy.AllowAll;
        var captures = 0;

        var image = ScreenshotTool.CaptureForeground(process.Id, policy, () => { captures++; return "selected-window-pixels"; });

        Assert.Equal("selected-window-pixels", image);
        Assert.Equal(1, captures);
    }
}
