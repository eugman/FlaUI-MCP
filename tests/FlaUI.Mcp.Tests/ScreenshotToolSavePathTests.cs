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
        Assert.Contains("absolute", error);
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
