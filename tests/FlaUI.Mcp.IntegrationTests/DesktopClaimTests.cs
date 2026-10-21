namespace FlaUI.Mcp.IntegrationTests;

/// <summary>
/// The desktop suite must not run beside a host or a recording that owns the desktop, and must close only its
/// own stray test apps, never another checkout's. Pure decisions; no app is launched.
/// </summary>
public sealed class DesktopClaimTests
{
    [Fact]
    public void ARunningHostMakesTheDesktopBusy() =>
        Assert.Equal(
            "FlaUI.Mcp.PowerBi (PID 42) is running and may be driving the desktop; stop it first.",
            DesktopClaim.BusyReason([("explorer", 1), ("FlaUI.Mcp.PowerBi", 42)], busy: null, self: 7)
        );

    [Fact]
    public void TheBusyVariableMakesTheDesktopBusy() =>
        Assert.StartsWith("FLAUI_DESKTOP_BUSY is set", DesktopClaim.BusyReason([], busy: "recording", self: 7));

    [Fact]
    public void TheTestHostItselfDoesNotCount() =>
        Assert.Null(DesktopClaim.BusyReason([("FlaUI.Mcp.IntegrationTests", 7), ("testhost", 8)], busy: null, self: 7));

    [Fact]
    public void OnlyTestAppsBuiltFromThisCheckoutAreOurs()
    {
        var root = @"C:\src\FlaUI-MCP-Core\tests\TestApps";

        Assert.True(
            DesktopClaim.IsOurTestApp(@"C:\src\FlaUI-MCP-Core\tests\TestApps\WpfTestApp\bin\Debug\WpfTestApp.exe", root)
        );
        Assert.False(
            DesktopClaim.IsOurTestApp(@"C:\src\FlaUI-MCP-PBI\tests\TestApps\WpfTestApp\bin\Debug\WpfTestApp.exe", root)
        );
        Assert.False(DesktopClaim.IsOurTestApp(@"C:\src\FlaUI-MCP-Core\tests\TestAppsOld\WpfTestApp.exe", root));
        Assert.False(DesktopClaim.IsOurTestApp(null, root));
    }
}
