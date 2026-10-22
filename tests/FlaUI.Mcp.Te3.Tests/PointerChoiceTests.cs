using FlaUI.Mcp.Core;
using Xunit;

/// <summary>
/// Where the page's own pointer moves go, chosen under the input lease right before the move: the park point on the
/// title bar from the window's visible frame, and the check that the point lies on the TE3 window. Pure inputs.
/// </summary>
public sealed class PointerChoiceTests
{
    [Fact]
    public void TheParkPointOnAMaximizedWindowIsOnItsVisibleTitleBar()
    {
        // A maximized window's rectangle runs 9 px past the monitor on each side (its invisible border); the visible
        // frame starts at the monitor's edge, and the title bar is 12 px below that, not 3.
        var bounds = new System.Drawing.Rectangle(-9, -9, 1938, 1098);
        var visible = new System.Drawing.Rectangle(0, 0, 1920, 1080);

        Assert.Equal(new System.Drawing.Point(960, 12), Te3Page.ParkPoint(bounds, visible));
        // A frame that does not meet the window (a stale read) changes nothing.
        Assert.Equal(
            Te3Page.ParkPoint(bounds),
            Te3Page.ParkPoint(bounds, new System.Drawing.Rectangle(5000, 5000, 10, 10))
        );
    }

    [Fact]
    public void APointAnotherWindowCoversIsRefusedBeforeTheMove()
    {
        var target = new InputTarget(100, 4242, 0);
        var point = new System.Drawing.Point(960, 12);

        Assert.Equal(point, Te3Page.OnWindow(point, 100, _ => 100, target, "TE3 title bar"));
        var refused = Assert.Throws<InputRefusedException>(() =>
            Te3Page.OnWindow(point, 100, _ => 900, target, "TE3 title bar")
        );

        Assert.Equal("point-not-on-target", refused.Refusal.Code);
    }
}
