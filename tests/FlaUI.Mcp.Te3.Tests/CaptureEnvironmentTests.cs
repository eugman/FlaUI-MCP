using System.Drawing;
using System.Text.Json;
using Xunit;

public sealed class CaptureEnvironmentTests
{
    [Fact]
    public void ExpectedScaleUsesObservedDpiNotVariantName()
    {
        var observed = new CaptureEnvironment(
            "DISPLAY1",
            new Rectangle(0, 0, 3840, 2160),
            new Rectangle(0, 0, 3840, 2100),
            new Rectangle(20, 30, 1600, 1000),
            144
        );
        Assert.Equal(150, observed.MainWindowScalePercent);
        observed.RequireScale(null);
        observed.RequireScale(144);
        Assert.Throws<InvalidOperationException>(() => observed.RequireScale(192));
        var json = JsonSerializer.Serialize(observed, RunConfig.Json);
        Assert.Equal(observed, JsonSerializer.Deserialize<CaptureEnvironment>(json, RunConfig.Json));
    }

    [Fact]
    public void OlderManifestHasNoInventedCaptureEnvironment()
    {
        var manifest = JsonSerializer.Deserialize<RunManifest>("{\"runId\":\"old\"}", RunConfig.Json)!;
        Assert.Null(manifest.CaptureVariant);
        Assert.Null(manifest.ExpectedDpi);
        Assert.Empty(manifest.CaptureEnvironments);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(120)]
    public void MismatchedWindowAndMonitorDpiCannotVerifyCapture(int? expectedDpi)
    {
        var observed = new CaptureEnvironment(
            "DISPLAY2",
            new Rectangle(0, 0, 1920, 1080),
            new Rectangle(0, 0, 1920, 1040),
            new Rectangle(0, 0, 1920, 1080),
            120,
            168
        );

        Assert.True(observed.DpiMismatch);
        var error = Assert.Throws<InvalidOperationException>(() => observed.RequireScale(expectedDpi));
        Assert.Contains("120", error.Message);
        Assert.Contains("168", error.Message);
        Assert.Contains("system-DPI-aware", error.Message);
        Assert.DoesNotContain("Reopen", error.Message);
    }
}
