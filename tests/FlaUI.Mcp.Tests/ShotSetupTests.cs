using System.Drawing;
using System.Text.Json;
using FlaUI.Core.Definitions;
using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>Window state, pointer geometry, click actions and selector strictness; no desktop access.</summary>
public sealed class ShotSetupTests
{
    [Theory]
    [InlineData("maximize", "maximized")]
    [InlineData("Maximized", "maximized")]
    [InlineData("restore", "normal")]
    [InlineData("normal", "normal")]
    [InlineData(" minimize ", "minimized")]
    public void WindowStateAcceptsVerbsAndStates(string input, string expected) =>
        Assert.Equal(expected, WindowStates.Parse(input));

    [Theory]
    [InlineData(null)]
    [InlineData("fullscreen")]
    public void WindowStateRejectsUnknown(string? input) =>
        Assert.Throws<ArgumentException>(() => WindowStates.Parse(input));

    [Fact]
    public async Task WindowStateNeedsATarget()
    {
        var tool = new WindowStateTool(new ElementRegistry(), new PendingInvokeTracker());
        var result = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new { state = "maximize" }));
        Assert.True(result.IsError);
        Assert.Contains("ref or handle required", result.Content[0].Text);
    }

    [Fact]
    public void DragPathEndsExactlyOnTarget()
    {
        var path = PointerPlan.DragPath(new Point(100, 200), new Point(403, 187), 7);
        Assert.Equal(7, path.Count);
        Assert.Equal(new Point(403, 187), path[^1]);
        Assert.All(path.Zip(path.Skip(1)), pair => Assert.True(pair.Second.X >= pair.First.X));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(PointerPlan.MaxDragSteps + 1)]
    public void DragPathRejectsBadStepCounts(int steps) =>
        Assert.Throws<ArgumentException>(() => PointerPlan.DragPath(Point.Empty, new Point(5, 5), steps));

    [Fact]
    public void ParkPrefersAnotherMonitor()
    {
        var primary = new Rectangle(0, 0, 1920, 1080);
        var second = new Rectangle(1920, 0, 1920, 1080);
        Assert.Equal(new Point(1928, 8), PointerPlan.ParkPoint(new Rectangle(10, 10, 1800, 1000), [primary, second]));
    }

    [Fact]
    public void ParkBesideANormalWindow()
    {
        var monitor = new Rectangle(0, 0, 2560, 1440);
        var window = new Rectangle(10, 10, 1900, 1040);
        Assert.Equal(new Point(1918, 530), PointerPlan.ParkPoint(window, [monitor]));
    }

    [Fact]
    public void ParkOnTheTaskbarBelowAMaximizedWindow()
    {
        // A maximized window's bounds overhang the work area by its frame, and the taskbar sits below.
        var monitor = new Rectangle(0, 0, 2560, 1440);
        var window = new Rectangle(-8, -8, 2576, 1376);
        var park = PointerPlan.ParkPoint(window, [monitor]);
        Assert.NotNull(park);
        Assert.False(window.Contains(park.Value));
        Assert.True(monitor.Contains(park.Value));
    }

    [Fact]
    public void NoParkWhenTheWindowCoversTheScreen() =>
        Assert.Null(PointerPlan.ParkPoint(new Rectangle(-8, -8, 1936, 1096), [new Rectangle(0, 0, 1920, 1080)]));

    [Fact]
    public void TreeRowsSelectInsteadOfInvoking() =>
        Assert.Equal("select", ClickTool.ChooseAction(null, false, ControlType.TreeItem, _ => true));

    [Fact]
    public void ButtonsStillInvoke() =>
        Assert.Equal("invoke", ClickTool.ChooseAction(null, false, ControlType.Button, _ => true));

    [Fact]
    public void MouseOnlyClicksUseNoPattern() =>
        Assert.Null(ClickTool.ChooseAction(null, true, ControlType.TreeItem, _ => true));

    [Fact]
    public void RequestedClickForcesTheMouse() =>
        Assert.Null(ClickTool.ChooseAction("click", false, ControlType.Button, _ => true));

    [Fact]
    public void ExpandIsHonouredOnATreeRow() =>
        Assert.Equal(
            "expand",
            ClickTool.ChooseAction("expand", false, ControlType.TreeItem, p => p is "expand" or "select")
        );

    [Fact]
    public void UnsupportedRequestedActionFailsBeforeInput() =>
        Assert.Throws<InvalidOperationException>(() =>
            ClickTool.ChooseAction("expand", false, ControlType.Button, p => p == "invoke")
        );

    [Fact]
    public void UnknownSelectorKeysAreErrors() =>
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<ElementSelector>("{\"nameContain\":\"Close\"}", FindTool.SelectorOptions)
        );

    [Fact]
    public void NameContainsIsASelectorField()
    {
        var selector = JsonSerializer.Deserialize<ElementSelector>(
            "{\"nameContains\":\"close\",\"controlType\":\"Button\"}",
            FindTool.SelectorOptions
        );
        Assert.Equal("close", selector!.NameContains);
        Assert.Equal("Button", selector.ControlType);
    }

    [Fact]
    public void ColumnDividerIsTheRightEdge() =>
        Assert.Equal(new Point(519, 25), PointerPlan.EdgePoint(new Rectangle(344, 2, 176, 46), "right"));

    [Fact]
    public void ResizeRightEdgeKeepsTheLeftEdge() =>
        Assert.Equal(new Point(593, 25), PointerPlan.ResizeTarget(new Rectangle(344, 2, 461, 46), "right", 250));

    [Fact]
    public void ResizeLeftEdgeKeepsTheRightEdge() =>
        Assert.Equal(new Point(1220, 400), PointerPlan.ResizeTarget(new Rectangle(849, 0, 1071, 800), "left", 700));

    [Fact]
    public void ResizeNeedsAnEdge() =>
        Assert.Throws<ArgumentException>(() => PointerPlan.ResizeTarget(new Rectangle(0, 0, 10, 10), "center", 5));
}
