using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// Every process-name read goes through InputDesktop.ProcessNameOf. A real lookup of a made-up PID can land on a live
/// process (Windows ignores a PID's low two bits), which made name assertions depend on the machine.
/// </summary>
public sealed class ProcessNameSeamTests
{
    private const string FakedName = "faked-app-name";

    private static FakeDesktop Named()
    {
        var desktop = new FakeDesktop();
        desktop.ProcessNames[FakeDesktop.OtherPid] = FakedName;
        return desktop;
    }

    [Fact]
    public void TheAllowlistReadsTheNameThroughTheSeam()
    {
        using var installed = Named().Install();

        Assert.True(new ProcessPolicy([FakedName]).IsProcessAllowed(FakeDesktop.OtherPid));
        Assert.False(new ProcessPolicy(["another-app"]).IsProcessAllowed(FakeDesktop.OtherPid));
    }

    [Fact]
    public void ARefusedWindowRegistrationNamesTheProcessFromTheSeam()
    {
        using var installed = Named().Install();
        using var sessions = new SessionManager(new ProcessPolicy(["another-app"]));

        var error = Assert.ThrowsAny<Exception>(() => sessions.RegisterNativeWindow(1, FakeDesktop.OtherPid));

        Assert.Contains($"Process '{FakedName}'", error.Message);
    }

    [Fact]
    public void ARefusedForegroundCaptureNamesTheProcessFromTheSeam()
    {
        using var installed = Named().Install();

        var error = Assert.Throws<UnauthorizedAccessException>(() =>
            ScreenshotChecks.CaptureForeground(FakeDesktop.OtherPid, new ProcessPolicy(["another-app"]), () => 0)
        );

        Assert.Contains($"process '{FakedName}'", error.Message);
    }
}
