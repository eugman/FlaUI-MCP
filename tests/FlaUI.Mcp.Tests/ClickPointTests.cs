using System.Drawing;
using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// Pointer actions on an element press exactly where the element is visible, or refuse. An offscreen
/// element reports empty bounds, whose centre (0,0) is the caption of a maximized window: a drag from
/// there moves the window.
/// </summary>
public sealed class ClickPointTests
{
    private static readonly Rectangle Row = new(100, 200, 300, 20);
    private static readonly Rectangle Viewport = new(90, 150, 400, 300);

    private static ClickGeometry Element(
        bool offscreen = false,
        Point? clickable = null,
        Rectangle? bounds = null,
        params Rectangle[] scrollers
    ) => new(offscreen, clickable, bounds ?? Row, scrollers);

    private static string Refusal(ClickGeometry geometry, Point? requested = null) =>
        Assert.Throws<InvalidOperationException>(() => ClickPoint.Choose(geometry, requested)).Message;

    [Fact]
    public void VisibleElementUsesItsClickablePoint()
    {
        Assert.Equal(new Point(150, 210), ClickPoint.Choose(Element(clickable: new(150, 210), scrollers: Viewport)));
    }

    [Fact]
    public void OffscreenElementIsRefused()
    {
        Assert.Contains("offscreen", Refusal(Element(offscreen: true, clickable: new(150, 210))));
    }

    [Fact]
    public void EmptyBoundsAreRefusedInsteadOfPressingTheScreenOrigin()
    {
        var message = Refusal(Element(bounds: Rectangle.Empty));
        Assert.Contains("no input sent", message);
    }

    [Fact]
    public void MissingClickablePointIsRefusedInsteadOfFallingBackToTheCentre()
    {
        Assert.Contains("no input sent", Refusal(Element()));
    }

    [Fact]
    public void ClickablePointOutsideTheElementIsRefused()
    {
        Assert.Contains("outside", Refusal(Element(clickable: new(50, 50))));
    }

    [Fact]
    public void PointScrolledOutOfAnAncestorViewportIsRefused()
    {
        var clipped = new Rectangle(90, 150, 400, 40); // the row at y=200..220 is below this viewport
        Assert.Contains("scrolled", Refusal(Element(clickable: new(150, 210), scrollers: [Viewport, clipped])));
    }

    [Fact]
    public void RequestedEdgePointMustAlsoBeVisible()
    {
        Assert.Equal(new Point(399, 210), ClickPoint.Choose(Element(scrollers: Viewport), new Point(399, 210)));
        Assert.Contains("outside", Refusal(Element(scrollers: Viewport), new Point(500, 210)));
    }
}
