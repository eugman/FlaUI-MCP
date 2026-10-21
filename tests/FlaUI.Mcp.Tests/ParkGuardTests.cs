using System.Drawing;
using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>Parking moves the real pointer, so it refuses when it cannot leave the window and checks integrity.</summary>
[Collection(InputLeaseCollection.Name)]
public sealed class ParkGuardTests
{
    [Fact]
    public void AWindowThatCoversItsMonitorIsRefusedWithNoInput()
    {
        var desktop = new FakeDesktop { MonitorBounds = [new(0, 0, 1920, 1080)] };
        using var installed = desktop.Install();

        var error = Assert.Throws<InvalidOperationException>(() =>
            PointerParking.Park(new Rectangle(-8, -8, 1936, 1096))
        );

        Assert.Contains("could not leave the window", error.Message);
        Assert.Contains("no input sent", error.Message);
        Assert.Equal(0, desktop.Dispatched);
    }

    [Fact]
    public void AHigherIntegrityWindowUnderTheParkPointIsRefused()
    {
        var desktop = new FakeDesktop
        {
            MonitorBounds = [new(0, 0, 1920, 1080)],
            TargetIntegrity = new(ProcessIntegrity.High, false),
        };
        using var installed = desktop.Install();

        var error = Assert.Throws<InputRefusedException>(() => PointerParking.Park(new Rectangle(10, 10, 800, 600)));

        Assert.Equal("integrity-blocked", error.Refusal.Code);
        Assert.Equal(0, desktop.Dispatched);
    }

    [Fact]
    public void ParkMovesThePointerBesideTheWindow()
    {
        var desktop = new FakeDesktop { MonitorBounds = [new(0, 0, 1920, 1080)] };
        using var installed = desktop.Install();

        var landed = PointerParking.Park(new Rectangle(10, 10, 800, 600));

        Assert.Equal(1, desktop.Dispatched);
        Assert.Equal(new Point(818, 310), landed);
    }
}
