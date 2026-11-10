using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

public class ScreenshotToolSavePathTests
{
    [Fact]
    public void TryNormalizeSavePath_RejectsRelativePath()
    {
        var ok = ScreenshotChecks.TryNormalizeSavePath("capture.png", overwrite: false, out _, out var error);

        Assert.False(ok);
        Assert.Contains("fully qualified", error);
    }

    [Theory]
    [InlineData("//server/share/capture.png")]
    [InlineData(@"\\?\C:\captures\capture.png")]
    [InlineData(@"\\.\C:\captures\capture.png")]
    [InlineData(@"C:\captures\..\..\capture.png")]
    public void TryNormalizeSavePath_RejectsEveryNetworkDeviceAndTraversalForm(string path)
    {
        // A screen capture must not be written to a network share: the forward-slash UNC form passed the old check.
        var ok = ScreenshotChecks.TryNormalizeSavePath(path, overwrite: false, out var normalized, out var error);

        Assert.False(ok);
        Assert.Null(normalized);
        Assert.StartsWith("savePath must", error);
    }

    [Theory]
    [InlineData(@"C:\captures\cost$\capture.png")]
    [InlineData(@"C:\captures\a$b.png")]
    [InlineData(@"C:\captures\%FLAUI_NO_SUCH_VARIABLE%\capture.png")]
    public void TryNormalizeSavePath_KeepsADollarOrAnUndefinedPercentNameAsLiteralText(string path)
    {
        // A tool argument is a file name someone chose, not a config value: these are ordinary characters in it.
        Assert.True(
            ScreenshotChecks.TryNormalizeSavePath(path, overwrite: false, out var normalized, out var error),
            error
        );
        Assert.Equal(path, normalized);
    }

    [Theory]
    [InlineData(@"C:\captures\$HOME\capture.png")]
    [InlineData(@"C:\captures\${HOME}\capture.png")]
    public void TryNormalizeSavePath_RefusesAShellVariableAtTheStartOfASegment(string path)
    {
        Assert.False(ScreenshotChecks.TryNormalizeSavePath(path, overwrite: false, out _, out var error));
        Assert.Contains("$ environment variable", error);
    }

    [Fact]
    public void AConfiguredPathStillRefusesAnyDollarAndAnUndefinedVariable()
    {
        Assert.Throws<ArgumentException>(() => HostPath.Validate(@"C:\captures\cost$\capture.png"));
        Assert.Throws<ArgumentException>(() => HostPath.Validate(@"C:\captures\%FLAUI_NO_SUCH_VARIABLE%\capture.png"));
    }

    [Fact]
    public void TryNormalizeSavePath_AcceptsALocalPngPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");

        Assert.True(ScreenshotChecks.TryNormalizeSavePath(path, overwrite: false, out var normalized, out _));
        Assert.Equal(Path.GetFullPath(path), normalized);
    }

    [Fact]
    public void TryNormalizeSavePath_RejectsUncPath()
    {
        var ok = ScreenshotChecks.TryNormalizeSavePath(
            @"\\server\share\capture.png",
            overwrite: false,
            out _,
            out var error
        );

        Assert.False(ok);
        Assert.Contains("UNC", error);
    }

    [Fact]
    public void TryNormalizeSavePath_RejectsNonPngPath()
    {
        var path = Path.Combine(Path.GetTempPath(), "capture.txt");
        var ok = ScreenshotChecks.TryNormalizeSavePath(path, overwrite: false, out _, out var error);

        Assert.False(ok);
        Assert.Contains(".png", error);
    }

    [Fact]
    public void TryNormalizeSavePath_RejectsExistingFileWithoutOverwrite()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        File.WriteAllText(path, "");

        try
        {
            var ok = ScreenshotChecks.TryNormalizeSavePath(path, overwrite: false, out _, out var error);

            Assert.False(ok);
            Assert.Contains("overwrite=true", error);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
