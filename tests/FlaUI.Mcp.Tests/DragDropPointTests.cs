using System.Drawing;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Mcp;
using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// Where a windows_mouse drag releases on a toRef. The live seed (TE3, 2026-10-22): a drop with toEdge left on a
/// TOM Explorer row was put on the row's outermost pixel, and the reply never said that the tool had scrolled the
/// row's view first, so the agent compared the drop with bounds from an earlier snapshot.
/// </summary>
[Collection(InputLeaseCollection.Name)]
public sealed class DragDropPointTests
{
    // The TOM Explorer row of the live seed, inside the fake desktop's window at (100,100,800,600).
    private static readonly Rectangle Row = new(433, 214, 435, 22);

    private static AutomationElement Element() =>
        (AutomationElement)RuntimeHelpers.GetUninitializedObject(typeof(AutomationElement));

    private static JsonElement Args(object value) => JsonSerializer.SerializeToElement(value);

    private static (ElementRegistry Refs, SessionManager Sessions, string Handle) Window()
    {
        var sessions = new SessionManager(null, ProcessPolicy.AllowAll);
        var handle = sessions.PublishWindow(null, 0, FakeDesktop.TargetPid, FakeDesktop.Target);
        return (new ElementRegistry(), sessions, handle);
    }

    private static Task<McpToolResult> Run(MouseTool tool, object arguments)
    {
        var registry = new ToolRegistry();
        registry.RegisterTool(tool);
        return registry.ExecuteToolAsync(tool.Name, Args(arguments));
    }

    private static JsonElement Reply(McpToolResult result) => JsonDocument.Parse(result.Content[0].Text!).RootElement;

    private static Point Screen(JsonElement reply) =>
        new(
            reply.GetProperty("screen").GetProperty("x").GetInt32(),
            reply.GetProperty("screen").GetProperty("y").GetInt32()
        );

    [Theory]
    [InlineData("left", 0, 100, 210)]
    [InlineData("right", 0, 399, 210)]
    [InlineData("top", 0, 250, 200)]
    [InlineData("bottom", 0, 250, 219)]
    [InlineData("center", 0, 250, 210)]
    [InlineData("left", 2, 102, 210)]
    [InlineData("right", 2, 397, 210)]
    [InlineData("top", 2, 250, 202)]
    [InlineData("bottom", 2, 250, 217)]
    [InlineData("center", 2, 250, 210)]
    public void AnEdgePointIsTheEdgesOwnPixelOrTheInsetInsideIt(string edge, int inset, int x, int y)
    {
        // Symmetric: left/top are the first pixel, right/bottom the last, each moved inward by the inset.
        Assert.Equal(new Point(x, y), PointerPlan.EdgePoint(new Rectangle(100, 200, 300, 20), edge, inset));
    }

    [Theory]
    [InlineData(3, 3, 1)]
    [InlineData(1, 1, 0)]
    [InlineData(4, 4, 1)]
    [InlineData(5, 5, 2)]
    public void AnInsetIsClampedInsideAThinRectangle(int width, int height, int expected)
    {
        var thin = new Rectangle(10, 10, width, height);

        Assert.Equal(new Point(10 + expected, thin.Y + height / 2), PointerPlan.EdgePoint(thin, "left", 2));
        Assert.Equal(new Point(10 + width / 2, 10 + expected), PointerPlan.EdgePoint(thin, "top", 2));
        Assert.Equal(
            new Point(10 + width - 1 - expected, thin.Y + height / 2),
            PointerPlan.EdgePoint(thin, "right", 2)
        );
        Assert.Equal(new Point(10 + width / 2, 10 + height - 1 - expected), PointerPlan.EdgePoint(thin, "bottom", 2));
    }

    [Fact]
    public void AnUnknownEdgeIsRefused()
    {
        Assert.Throws<ArgumentException>(() => PointerPlan.EdgePoint(Row, "middle"));
    }

    [Fact]
    public async Task ADropOnAnEdgeLandsInsideTheRowNotOnItsBorderPixel()
    {
        // The live seed: ref and toRef the same row, toEdge left. The drop was on the row's first pixel (x 433),
        // which a tree draws as the gutter beside the neighbouring pane; it now sits DropInset pixels in.
        var row = Element();
        var desktop = new FakeDesktop();
        desktop.ElementBounds[row] = Row;
        using var installed = desktop.Install();
        var (refs, sessions, handle) = Window();
        using var _ = sessions;
        var reference = refs.Register(handle, row);

        var result = await Run(
            new MouseTool(refs, new PendingInvokeTracker(), sessions),
            new
            {
                action = "drag",
                @ref = reference,
                toRef = reference,
                toEdge = "left",
            }
        );

        Assert.True(result.IsError != true, result.Content[0].Text);
        Assert.Equal(new Point(Row.Left + PointerPlan.DropInset, 225), Screen(Reply(result)));
        // Move to the start, press, twelve moves, release: the live run's 15 inputs.
        Assert.Equal(15, result.Outcome!.InputsSent);
        Assert.Equal("target", result.Outcome.HitTest);
    }

    [Theory]
    [InlineData("right", 433 + 435 - 1 - PointerPlan.DropInset, 225)]
    [InlineData("top", 650, 214 + PointerPlan.DropInset)]
    [InlineData("bottom", 650, 214 + 22 - 1 - PointerPlan.DropInset)]
    [InlineData("center", 650, 225)]
    public async Task EveryDropEdgeKeepsTheSameInset(string edge, int x, int y)
    {
        var row = Element();
        var desktop = new FakeDesktop();
        desktop.ElementBounds[row] = Row;
        using var installed = desktop.Install();
        var (refs, sessions, handle) = Window();
        using var _ = sessions;
        var reference = refs.Register(handle, row);

        var result = await Run(
            new MouseTool(refs, new PendingInvokeTracker(), sessions),
            new
            {
                action = "drag",
                @ref = reference,
                toRef = reference,
                toEdge = edge,
            }
        );

        Assert.True(result.IsError != true, result.Content[0].Text);
        Assert.Equal(new Point(x, y), Screen(Reply(result)));
    }

    [Fact]
    public async Task AStartEdgeStaysOnTheEdgesOwnPixel()
    {
        // A column header's divider is its last pixel: fromEdge keeps grabbing exactly there.
        var header = Element();
        var desktop = new FakeDesktop();
        // The header reaches the requested width once the button is up (move, press, one move, release).
        desktop.OnGeometry = _ => new ClickGeometry(
            false,
            null,
            desktop.Dispatched >= 4 ? new Rectangle(433, 214, 300, 22) : Row,
            []
        );
        using var installed = desktop.Install();
        var (refs, sessions, handle) = Window();
        using var _ = sessions;
        var reference = refs.Register(handle, header);

        var result = await Run(
            new MouseTool(refs, new PendingInvokeTracker(), sessions),
            new
            {
                action = "drag",
                @ref = reference,
                fromEdge = "right",
                resizeTo = 300,
                steps = 1,
            }
        );

        Assert.True(result.IsError != true, result.Content[0].Text);
        Assert.Equal(new Point(Row.Left + 300 - 1, 225), Screen(Reply(result)));
    }

    [Fact]
    public async Task TheReplySaysWhenTheToolScrolledTheViewFirst()
    {
        // Scrolling ref and toRef into view are the tool's own changes before the press; the row may sit
        // elsewhere than in the agent's last snapshot afterwards, so a successful reply says so too.
        var row = Element();
        var desktop = new FakeDesktop { ScrollNote = "the view was scrolled" };
        desktop.ElementBounds[row] = Row;
        using var installed = desktop.Install();
        var (refs, sessions, handle) = Window();
        using var _ = sessions;
        var reference = refs.Register(handle, row);

        var result = await Run(
            new MouseTool(refs, new PendingInvokeTracker(), sessions),
            new
            {
                action = "drag",
                @ref = reference,
                toRef = reference,
                toEdge = "left",
            }
        );

        Assert.True(result.IsError != true, result.Content[0].Text);
        Assert.Equal("the view was scrolled", Reply(result).GetProperty("changed").GetString());
        Assert.Equal(2, desktop.ScrollRequests);
    }

    [Fact]
    public async Task TheReplyLeavesChangedOutWhenNothingScrolled()
    {
        var row = Element();
        var desktop = new FakeDesktop();
        desktop.ElementBounds[row] = Row;
        using var installed = desktop.Install();
        var (refs, sessions, handle) = Window();
        using var _ = sessions;
        var reference = refs.Register(handle, row);

        var result = await Run(
            new MouseTool(refs, new PendingInvokeTracker(), sessions),
            new { action = "click", @ref = reference }
        );

        Assert.True(result.IsError != true, result.Content[0].Text);
        Assert.False(Reply(result).TryGetProperty("changed", out var changed), changed.ToString());
    }

    [Fact]
    public void ADropPointInTheTopAutoScrollBandIsRefusedBeforeInput()
    {
        // The live seed: the first visible row of a tree; a drag resting on it scrolled the tree six rows.
        var tree = new Rectangle(427, 214, 467, 400);
        var geometry = new ClickGeometry(false, null, Row, [tree]);

        var error = Assert.Throws<InputRefusedException>(() =>
            ClickPoint.RequireOutsideScrollBand(new Point(435, 225), geometry)
        );

        Assert.Equal("drop-point-in-scroll-band", error.Refusal.Code);
        Assert.Equal(
            "The drop point (435,225) is within 22 px of the top of a scrolling list or pane (its viewport is "
                + "(427,214 467x400)); a drag resting there scrolls the view, so the release would land on another "
                + "row; no input sent. Scroll the view so the target is away from that edge, then observe and retry.",
            error.Message
        );
    }

    [Theory]
    [InlineData(592, "bottom")]
    [InlineData(613, "bottom")]
    [InlineData(235, "top")]
    public void TheBandIsOneRowAtEitherEnd(int y, string edge)
    {
        var tree = new Rectangle(427, 214, 467, 400);
        var geometry = new ClickGeometry(false, null, Row, [tree]);

        var error = Assert.Throws<InputRefusedException>(() =>
            ClickPoint.RequireOutsideScrollBand(new Point(435, y), geometry)
        );

        Assert.Contains($"of the {edge} of a scrolling", error.Message);
    }

    [Theory]
    [InlineData(236)]
    [InlineData(400)]
    [InlineData(591)]
    public void ADropPointBetweenTheBandsPasses(int y)
    {
        var tree = new Rectangle(427, 214, 467, 400);

        ClickPoint.RequireOutsideScrollBand(new Point(435, y), new ClickGeometry(false, null, Row, [tree]));
    }

    [Fact]
    public void AViewportShorterThanThreeRowsHasNoBand()
    {
        var shortList = new Rectangle(427, 214, 467, 65);

        ClickPoint.RequireOutsideScrollBand(new Point(435, 225), new ClickGeometry(false, null, Row, [shortList]));
        ClickPoint.RequireOutsideScrollBand(new Point(435, 225), new ClickGeometry(false, null, Row, []));
    }

    [Fact]
    public async Task ADragOntoTheFirstVisibleRowIsRefusedWithNothingSent()
    {
        var row = Element();
        var desktop = new FakeDesktop();
        desktop.ElementBounds[row] = Row;
        desktop.Viewports[row] = [new Rectangle(427, 214, 467, 400)];
        using var installed = desktop.Install();
        var (refs, sessions, handle) = Window();
        using var _ = sessions;
        var reference = refs.Register(handle, row);

        var result = await Run(
            new MouseTool(refs, new PendingInvokeTracker(), sessions),
            new
            {
                action = "drag",
                @ref = reference,
                toRef = reference,
                toEdge = "bottom",
            }
        );

        Assert.True(result.IsError);
        Assert.StartsWith(
            "Failed to drag: The drop point (650,233) is within 22 px of the top",
            result.Content[0].Text
        );
        Assert.Equal("drop-point-in-scroll-band", result.Outcome!.Failure!.Code);
        Assert.Equal("not-dispatched", result.Outcome.Dispatch);
        Assert.Equal(0, desktop.Dispatched);
    }

    [Fact]
    public async Task ADragOntoARowAwayFromTheEdgesIsSent()
    {
        var row = Element();
        var desktop = new FakeDesktop();
        desktop.ElementBounds[row] = Row;
        desktop.Viewports[row] = [new Rectangle(427, 150, 467, 400)];
        using var installed = desktop.Install();
        var (refs, sessions, handle) = Window();
        using var _ = sessions;
        var reference = refs.Register(handle, row);

        var result = await Run(
            new MouseTool(refs, new PendingInvokeTracker(), sessions),
            new
            {
                action = "drag",
                @ref = reference,
                toRef = reference,
            }
        );

        Assert.True(result.IsError != true, result.Content[0].Text);
        Assert.Equal(15, desktop.Dispatched);
    }
}
