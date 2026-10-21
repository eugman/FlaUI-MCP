using System.Drawing;
using System.Runtime.CompilerServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// A scrolling ancestor's own scrollbars and headers are not part of its viewport, so a point under them is
/// pressed on the visible part of the element instead, and the press point is hit-tested before any input: a
/// point that lands on a sibling, a scrollbar or an overlay is refused by name with nothing sent.
/// </summary>
public sealed class ClickPointVisibilityTests
{
    // The live seed: a timeline clip body whose centre (548,1121) sits under the horizontal scrollbar.
    private static readonly Rectangle Timeline = new(0, 900, 1600, 240);
    private static readonly Rectangle HorizontalBar = new(0, 1118, 1600, 22);
    private static readonly Rectangle Clip = new(448, 1101, 200, 40);

    private static ClickGeometry Visible => new(false, null, Clip, [ClickGeometry.Viewport(Timeline, [HorizontalBar])]);

    private static AutomationElement Element() =>
        (AutomationElement)RuntimeHelpers.GetUninitializedObject(typeof(AutomationElement));

    [Fact]
    public void AViewportExcludesTheScrollbarAlongItsBottom() =>
        Assert.Equal(new Rectangle(0, 900, 1600, 218), ClickGeometry.Viewport(Timeline, [HorizontalBar]));

    [Fact]
    public void AViewportExcludesAVerticalScrollbarAndAColumnHeader()
    {
        var grid = new Rectangle(0, 0, 500, 400);
        var vertical = new Rectangle(480, 0, 20, 400);
        var header = new Rectangle(0, 0, 480, 24);

        Assert.Equal(Rectangle.FromLTRB(0, 24, 480, 400), ClickGeometry.Viewport(grid, [vertical, header]));
    }

    [Fact]
    public void AColumnHeaderEdgeIsInsideItsGridsViewport()
    {
        // The live failure: a resize drag from a grid column header's right edge, (-1227,233). The header row is
        // subtracted from the grid's viewport for content, but the header item itself lives in it.
        var grid = new Rectangle(-1500, 200, 800, 400);
        var headerRow = new Rectangle(-1500, 221, 780, 25);
        var bar = new Rectangle(-720, 200, 20, 400);
        var header = new Rectangle(-1400, 221, 174, 25);
        var viewport = ClickGeometry.Viewport(grid, [headerRow, bar], header);
        var geometry = new ClickGeometry(false, null, header, [viewport]);
        var edge = FlaUI.Mcp.Core.PointerPlan.EdgePoint(header, "right");

        Assert.Equal(new Point(-1227, 233), edge);
        Assert.Equal(edge, ClickPoint.Choose(geometry, edge));
        // The scrollbar is still cut off for it, and the header row still is for content.
        Assert.Equal(-720, viewport.Right);
        Assert.Equal(246, ClickGeometry.Viewport(grid, [headerRow, bar], new Rectangle(-1400, 300, 174, 20)).Top);
    }

    [Fact]
    public void ACentreUnderTheScrollbarFallsBackToTheCentreOfTheVisiblePart()
    {
        var geometry = new ClickGeometry(false, null, Clip, [ClickGeometry.Viewport(Timeline, [HorizontalBar])]);

        // Visible rows 1101..1117: the centre of that band, not (548,1121) on the scrollbar.
        Assert.Equal(new Point(548, 1109), ClickPoint.Choose(geometry));
    }

    [Fact]
    public void AClickablePointUnderTheScrollbarAlsoFallsBack()
    {
        var geometry = new ClickGeometry(
            false,
            new Point(500, 1130),
            Clip,
            [ClickGeometry.Viewport(Timeline, [HorizontalBar])]
        );

        Assert.Equal(new Point(548, 1109), ClickPoint.Choose(geometry));
    }

    [Fact]
    public void AnElementEntirelyUnderTheScrollbarIsRefused()
    {
        var hidden = new Rectangle(448, 1120, 200, 18);
        var geometry = new ClickGeometry(false, null, hidden, [ClickGeometry.Viewport(Timeline, [HorizontalBar])]);

        var error = Assert.Throws<InputRefusedException>(() => ClickPoint.Choose(geometry));
        Assert.Equal("click-point-unavailable", error.Refusal.Code);
    }

    [Fact]
    public void ARequestedEdgePointIsNeverMoved()
    {
        var geometry = new ClickGeometry(false, null, Clip, [ClickGeometry.Viewport(Timeline, [HorizontalBar])]);

        Assert.Throws<InputRefusedException>(() => ClickPoint.Choose(geometry, new Point(548, 1140)));
    }

    [Fact]
    public void APointOnTheTargetItselfIsAccepted()
    {
        var target = Element();
        using var installed = new FakeDesktop { HitTest = _ => target }.Install();

        ClickPoint.Verify(new Point(548, 1109), target);
    }

    [Fact]
    public void APointOnADescendantIsAccepted()
    {
        var target = Element();
        var label = Element();
        var desktop = new FakeDesktop { HitTest = _ => label };
        desktop.Parents[label] = target;
        using var installed = desktop.Install();

        ClickPoint.Verify(new Point(548, 1109), target);
    }

    [Fact]
    public void APointOnASiblingIsRefusedByName()
    {
        var target = Element();
        var scrollbar = Element();
        var parent = Element();
        var desktop = new FakeDesktop { HitTest = _ => scrollbar };
        desktop.Parents[scrollbar] = parent;
        desktop.Parents[target] = parent;
        desktop.Descriptions[scrollbar] = "ScrollBar 'PART_HorizontalScrollBar'";
        using var installed = desktop.Install();

        var error = Assert.Throws<InputRefusedException>(() => ClickPoint.Verify(new Point(548, 1121), target));

        Assert.Equal("click-point-covered", error.Refusal.Code);
        Assert.Equal(
            "The point (548,1121) is covered by ScrollBar 'PART_HorizontalScrollBar'; no input sent. Scroll the element into view, then observe again.",
            error.Message
        );
        Assert.Equal(0, desktop.Dispatched);
    }

    /// <summary>The press point Choose produces for the clip: the centre of its visible band.</summary>
    private static Point Pressed => ClickPoint.Choose(Visible);

    /// <summary>A chain target, then its ancestors upwards, each a parent of the one before.</summary>
    private static AutomationElement[] Chain(FakeDesktop desktop, params ControlType[] types)
    {
        var chain = types.Select(_ => Element()).ToArray();
        for (var i = 0; i < chain.Length; i++)
        {
            desktop.ControlTypes[chain[i]] = types[i];
            if (i > 0)
            {
                desktop.Parents[chain[i - 1]] = chain[i];
            }
        }

        return chain;
    }

    [Fact]
    public void AContainerHitNearTheTargetIsAcceptedAndReportedAsAnAncestor()
    {
        // WinForms PropertyGrid rows (MSAA), Chromium nodes and WPF text children report their container.
        var desktop = new FakeDesktop();
        var chain = Chain(desktop, ControlType.Custom, ControlType.List);
        desktop.HitTest = _ => chain[1];
        using var installed = desktop.Install();

        Assert.Equal("ancestor", ClickPoint.Verify(Pressed, chain[0]));
    }

    [Fact]
    public void TheTopLevelWindowIsNotAcceptedAsTheTargetsContainer()
    {
        var desktop = new FakeDesktop();
        var chain = Chain(desktop, ControlType.Custom, ControlType.Window);
        desktop.HitTest = _ => chain[1];
        desktop.Descriptions[chain[1]] = "Window 'Camtasia'";
        using var installed = desktop.Install();

        var error = Assert.Throws<InputRefusedException>(() => ClickPoint.Verify(Pressed, chain[0]));
        Assert.Contains("covered by Window 'Camtasia'", error.Message);
    }

    [Fact]
    public void AContainerMoreThanThreeLevelsUpIsNotAccepted()
    {
        var desktop = new FakeDesktop();
        var chain = Chain(
            desktop,
            ControlType.Custom,
            ControlType.Group,
            ControlType.Group,
            ControlType.Group,
            ControlType.Pane
        );
        desktop.HitTest = _ => chain[4];
        using var installed = desktop.Install();

        Assert.Throws<InputRefusedException>(() => ClickPoint.Verify(Pressed, chain[0]));
    }

    [Fact]
    public void ASiblingOverlayOverThePointIsRefusedEvenWhenTheContainerAnswers()
    {
        // The timeline pane answers the hit test; its scrollbar, a sibling of the clip, lies over the point.
        var desktop = new FakeDesktop();
        var chain = Chain(desktop, ControlType.Custom, ControlType.Pane);
        var overlay = Element();
        desktop.Parents[overlay] = chain[1];
        desktop.ElementBounds[overlay] = new Rectangle(Pressed.X - 10, Pressed.Y - 5, 20, 10);
        desktop.Descriptions[overlay] = "ScrollBar 'PART_HorizontalScrollBar'";
        desktop.HitTest = _ => chain[1];
        using var installed = desktop.Install();

        var error = Assert.Throws<InputRefusedException>(() => ClickPoint.Verify(Pressed, chain[0]));
        Assert.Contains("covered by ScrollBar 'PART_HorizontalScrollBar'", error.Message);
    }

    [Fact]
    public void ADirectHitIsReportedAsTheTargetAndAChildAsADescendant()
    {
        var desktop = new FakeDesktop();
        var chain = Chain(desktop, ControlType.Text, ControlType.Custom);
        using var installed = desktop.Install();

        desktop.HitTest = _ => chain[1];
        Assert.Equal("target", ClickPoint.Verify(Pressed, chain[1]));
        desktop.HitTest = _ => chain[0];
        Assert.Equal("descendant", ClickPoint.Verify(Pressed, chain[1]));
    }

    [Fact]
    public void AHitTestThatFailsIsARefusalNotAPass()
    {
        var desktop = new FakeDesktop { HitTest = _ => throw new TimeoutException("provider blocked") };
        using var installed = desktop.Install();

        var error = Assert.Throws<InputRefusedException>(() => ClickPoint.Verify(new Point(1, 1), Element()));

        Assert.Equal("click-point-covered", error.Refusal.Code);
        Assert.Contains("could not be hit-tested", error.Message);
    }

    [Fact]
    public async Task TheReplyCarriesHowThePointWasProven()
    {
        var desktop = new FakeDesktop();
        var chain = Chain(desktop, ControlType.Custom, ControlType.List);
        desktop.HitTest = _ => chain[1];
        using var installed = desktop.Install();
        var registry = new ToolRegistry();
        registry.RegisterTool(new VerifiesThenReplies(() => ClickPoint.Verify(Pressed, chain[0])));

        var result = await registry.ExecuteToolAsync("verifies", null);

        Assert.Equal("ancestor", result.Outcome!.HitTest);
    }

    private sealed class VerifiesThenReplies(Action verify) : ToolBase
    {
        public override string Name => "verifies";
        public override string Description => "no desktop";
        public override object InputSchema => new { };

        public override Task<McpToolResult> ExecuteAsync(System.Text.Json.JsonElement? arguments)
        {
            verify();
            return Task.FromResult(TextResult("Clicked"));
        }
    }
}
