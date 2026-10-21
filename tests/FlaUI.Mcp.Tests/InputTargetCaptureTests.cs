using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>Capturing a target reads its owner through the desktop seam, so it is checked offline.</summary>
public sealed class InputTargetCaptureTests
{
    [Fact]
    public void AWindowOwnedByTheProcessIsCaptured()
    {
        using var installed = new FakeDesktop().Install();

        var target = InputTarget.Capture(FakeDesktop.TargetHwnd, FakeDesktop.TargetPid);

        Assert.Equal(FakeDesktop.TargetHwnd, target.Hwnd);
        Assert.Equal(FakeDesktop.TargetPid, target.ProcessId);
    }

    [Fact]
    public void NoWindowOrAnotherProcessesWindowIsRefused()
    {
        using var installed = new FakeDesktop().Install();

        Assert.Throws<InvalidOperationException>(() => InputTarget.Capture(0, FakeDesktop.TargetPid));
        Assert.Throws<InvalidOperationException>(() =>
            InputTarget.Capture(FakeDesktop.OtherHwnd, FakeDesktop.TargetPid)
        );
    }
}
