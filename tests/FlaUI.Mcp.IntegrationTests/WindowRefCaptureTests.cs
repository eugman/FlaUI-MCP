using System.Drawing;
using FlaUI.Core.Definitions;
using PlaywrightWindows.Mcp.Tools;

namespace FlaUI.Mcp.IntegrationTests;

[Collection("TestApps")]
public sealed class WindowRefCaptureTests(TestAppFixture fixture)
{
    [Fact]
    public async Task NativeWindowRefProducesDecodablePng()
    {
        var window = fixture.GetWinFormsWindow()!;
        var reference = fixture.Elements.Register(fixture.WinFormsHandle, window);
        var path = Path.Combine(Path.GetTempPath(), $"fla_capture_{Guid.NewGuid():N}.png");
        try
        {
            var result = await fixture.CallTool(new ScreenshotTool(fixture.Session, fixture.Elements),
                new { @ref = reference, background = true, savePath = path, includeImage = false });
            Assert.True(File.Exists(path), result);
            using var image = Image.FromFile(path);
            Assert.True(image.Width > 100 && image.Height > 100);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task NativeRefRejectsNonWindowWithoutCreatingImage()
    {
        var button = fixture.GetWinFormsWindow()!.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button));
        Assert.NotNull(button);
        var reference = fixture.Elements.Register(fixture.WinFormsHandle, button);
        var path = Path.Combine(Path.GetTempPath(), $"fla_capture_{Guid.NewGuid():N}.png");
        try
        {
            var result = await fixture.CallTool(new ScreenshotTool(fixture.Session, fixture.Elements),
                new { @ref = reference, background = true, savePath = path, includeImage = false });
            Assert.Contains("requires a Window element", result);
            Assert.False(File.Exists(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
