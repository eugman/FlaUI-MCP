using System.Drawing;
using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

public sealed class ScalarSceneTests
{
    [Fact]
    public void ShadowExceptionIsRestrictedToObservedAppAndGeometry()
    {
        var dialog = new Rectangle(87, 183, 555, 254);
        var shadow = Rectangle.Inflate(dialog, 10, 10);
        var window = new FlaUI.Mcp.Core.Win32WindowInfo(123, "", 42, false, true, false);
        Assert.True(Te3Page.IsPopupShadow(window, shadow, 42, dialog));
        Assert.False(Te3Page.IsPopupShadow(window, shadow, 43, dialog));
        Assert.False(Te3Page.IsPopupShadow(window with { Title = "Other" }, shadow, 42, dialog));
        Assert.False(Te3Page.IsPopupShadow(window, dialog, 42, dialog));
        Assert.False(Te3Page.IsPopupShadow(window, null, 42, dialog));
        Assert.False(Te3Page.IsPopupShadow(window, shadow, 42, dialog, modal: false));
        Assert.True(Te3Page.IsPopupShadow(window, Rectangle.Inflate(dialog, 7, 7), 42, dialog, modal: false));
        Assert.True(Te3Page.IsPopupShadow(window, shadow, 42, dialog, modal: false, padding: 10));
        Assert.False(
            Te3Page.IsPopupShadow(window, Rectangle.Inflate(dialog, 7, 7), 42, dialog, modal: false, padding: 10)
        );
        Assert.False(Te3Page.IsPopupShadow(window, shadow, 43, dialog, modal: false, padding: 10));
        Assert.False(
            Te3Page.IsPopupShadow(window with { Title = "Other" }, shadow, 42, dialog, modal: false, padding: 10)
        );
    }

    [Fact]
    public void ExcludesInvisibleMaximizedBorder()
    {
        var scene = Te3Page.ScalarSceneBounds(new(-9, -9, 1938, 1158), new(87, 183, 555, 254), new(0, 0, 1920, 1140));
        Assert.Equal(new Rectangle(0, 91, 704, 361), scene);
    }

    [Fact]
    public void RetainsLargerOsConstrainedDialogOnNegativeMonitor()
    {
        var dialog = new Rectangle(-1800, 192, 900, 600);
        var scene = Te3Page.ScalarSceneBounds(new(-1929, -9, 1938, 1158), dialog, new(-1920, 0, 1920, 1032));
        Assert.Equal(-1920, scene.Left);
        Assert.True(scene.Contains(dialog));
        Assert.Equal(dialog.Bottom + 15, scene.Bottom);
        Assert.Equal(dialog.Right + 15, scene.Right);
    }

    [Fact]
    public void ThePointerParksInsideTheWindowOnANegativeCoordinateMonitor()
    {
        // A monitor above or left of the primary has negative desktop coordinates. Clamping to
        // screen zero put the park point on a different monitor, where the obscured-window check
        // then aborted a recipe whose selection had already succeeded.
        var bounds = new System.Drawing.Rectangle(-1920, -1080, 1280, 720);
        var point = Te3Page.ParkPoint(bounds);
        Assert.True(bounds.Contains(point), $"{point} is outside {bounds}");
    }

    [Theory]
    [InlineData(0, 0, 1920, 1080)]
    [InlineData(-1920, -1080, 1280, 720)]
    [InlineData(100, 50, 200, 20)]
    public void TheParkPointIsAlwaysInsideTheWindow(int x, int y, int width, int height)
    {
        // Including a window shorter than the 12px title-bar offset, which must not park below it.
        var bounds = new System.Drawing.Rectangle(x, y, width, height);
        Assert.True(bounds.Contains(Te3Page.ParkPoint(bounds)));
    }
}
