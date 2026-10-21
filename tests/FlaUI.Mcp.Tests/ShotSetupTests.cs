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
        using var offlineDesktop = new FakeDesktop().Install();
        var tool = new WindowStateTool(new ElementRegistry(), new PendingInvokeTracker());
        var result = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new { state = "maximize" }));
        Assert.True(result.IsError);
        Assert.Contains("ref or handle required", result.Content[0].Text);
    }

    [Fact]
    public void CoordinateClickIsInMouseSchema()
    {
        using var offlineDesktop = new FakeDesktop().Install();
        var tool = new MouseTool(new ElementRegistry(), new PendingInvokeTracker(), new SessionManager());
        var schema = JsonSerializer.SerializeToElement(tool.InputSchema);
        var actions = schema.GetProperty("properties").GetProperty("action").GetProperty("enum");
        Assert.Contains(actions.EnumerateArray(), value => value.GetString() == "click");
        Assert.Contains("left-click", tool.Description);
    }

    [Fact]
    public async Task CoordinateClickWithoutTargetReturnsBeforeInput()
    {
        using var offlineDesktop = new FakeDesktop().Install();
        var tool = new MouseTool(new ElementRegistry(), new PendingInvokeTracker(), new SessionManager());
        var result = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { action = "click", to = new { x = 10, y = 20 } })
        );
        Assert.True(result.IsError);
        Assert.Contains("handle or ref required", result.Content[0].Text);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    public void CoordinateClickRejectsOffWindowOrOccludedPoint(int hit)
    {
        var target = new InputTarget(42, 1, 1);
        Assert.Throws<InputRefusedException>(() =>
            PointerActions.RequireHit(target, new Point(10, 20), _ => (nint)hit)
        );
    }

    [Fact]
    public void CoordinateClickDoesNotReplayAfterHitFailure()
    {
        var target = new InputTarget(42, 1, 1);
        var clicks = 0;
        Assert.Throws<InputRefusedException>(() =>
            PointerActions.ClickGuarded(target, new Point(10, 20), _ => clicks++, _ => 99)
        );
        Assert.Equal(0, clicks);
        PointerActions.ClickGuarded(target, new Point(10, 20), _ => clicks++, _ => 42);
        Assert.Equal(1, clicks);
    }

    [Fact]
    public void CoordinateClickAcceptsTheCapturedWindowHit()
    {
        var target = new InputTarget(42, 1, 1, 43);
        Assert.Null(Record.Exception(() => PointerActions.RequireHit(target, new Point(10, 20), _ => 43)));
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

    [Fact]
    public void StructuredContentCarriesTheToolMessage()
    {
        var result = new McpToolResult
        {
            Content = [new McpContent { Text = "Selected Node8" }],
            Outcome = new ToolOutcome("completed", "returned"),
        };
        var json = JsonSerializer.Serialize(result);
        Assert.Contains("\"structuredContent\"", json);
        Assert.Contains("\"message\":\"Selected Node8\"", json);
    }

    [Fact]
    public void GuardChecksOnlyTheCroppedRegion()
    {
        var source = new Rectangle(920, 128, 1000, 892);
        var frame = new CaptureFrame(null, null, 10, 20, 300, 200);
        Assert.Equal(new Rectangle(930, 148, 300, 200), ScreenshotTool.GuardRect(source, frame));
    }

    [Fact]
    public void GuardWithoutAFrameChecksTheWholeCapture()
    {
        var source = new Rectangle(0, 43, 1920, 1073);
        Assert.Equal(source, ScreenshotTool.GuardRect(source, null));
    }

    [Fact]
    public void AWindowOutsideTheCropNoLongerRefuses()
    {
        var source = new Rectangle(920, 128, 1000, 892);
        var crop = ScreenshotTool.GuardRect(source, new CaptureFrame(null, null, 0, 0, 400, 300));
        Win32Desktop.WindowBounds W(nint h, int pid, Rectangle r) =>
            new(new Win32WindowInfo(h, "w", pid, IsEnabled: true, IsToolWindow: false, IsCloaked: false), r, true);
        // A notification over the pane's bottom-right corner, far from the 400x300 crop at its top-left.
        var zOrder = new[] { W(2, 200, new Rectangle(1600, 800, 300, 200)), W(1, 100, source) };
        Assert.True(Win32Desktop.FindObstructions(zOrder, 1, 100, crop).IsClean);
        Assert.False(Win32Desktop.FindObstructions(zOrder, 1, 100, source).IsClean);
    }

    [Theory]
    [InlineData(ToggleState.Off, ToggleState.On, "Show changes: Off -> On")]
    [InlineData(ToggleState.On, ToggleState.On, "Toggled Show changes, but it is still On")]
    public void ToggleReportsTheStateItReached(ToggleState before, ToggleState after, string expected) =>
        Assert.Equal(expected, ClickTool.ToggleOutcome("Show changes", before, after));

    [Theory]
    [InlineData(true, "Selected Node9 after fallback click (SelectionItem confirmed)")]
    [InlineData(
        false,
        "Clicked Node9 after Select changed nothing; selection was not observed. Inspect the current selection before continuing; do not repeat the click blindly."
    )]
    [InlineData(
        null,
        "Clicked Node9 after Select changed nothing; selection could not be read. Inspect the current selection before continuing; do not repeat the click blindly."
    )]
    public void FallbackClickReportsObservedSelection(bool? selected, string expected) =>
        Assert.Equal(expected, ClickTool.SelectionClickOutcome("Node9", selected));

    [Fact]
    public void SelectionObservationWaitsForProviderToSettle()
    {
        var reads = 0;
        var result = ClickTool.ObserveSelection(() => ++reads >= 2);
        Assert.True(result);
        Assert.Equal(2, reads);
    }

    [Fact]
    public void SelectionObservationReportsAnUnreadableProviderWithoutReplayingInput()
    {
        var reads = 0;
        var result = ClickTool.ObserveSelection(() =>
        {
            reads++;
            throw new InvalidOperationException("Provider unavailable");
        });
        Assert.Null(result);
        Assert.Equal(1, reads);
    }

    [Fact]
    public void ToggleWithUnreadableStateSaysSo() =>
        Assert.Contains("could not be read", ClickTool.ToggleOutcome("X", null, ToggleState.On));

    [Theory]
    [InlineData("", "COMException", "windows_wait: COMException No input sent.")]
    [InlineData("Element not available.", "X", "windows_wait: Element not available. No input sent.")]
    public void WaitFailuresAlwaysNameAReason(string message, string type, string expected) =>
        Assert.Equal(expected, WaitTool.FailureText(message, type));

    private static Win32WindowInfo Top(nint hwnd) =>
        new(hwnd, "Main window", 1, IsEnabled: true, IsToolWindow: false, IsCloaked: false);

    [Fact]
    public void LaunchWaitsForTheSameWindowOnTwoPolls()
    {
        Assert.Null(SessionManager.StableLaunchWindow(0, [Top(7)]));
        Assert.Equal((nint)7, SessionManager.StableLaunchWindow(7, [Top(7)]));
    }

    [Fact]
    public void LaunchIgnoresNoWindowOrSeveral()
    {
        Assert.Null(SessionManager.StableLaunchWindow(7, []));
        Assert.Null(SessionManager.StableLaunchWindow(7, [Top(7), Top(8)]));
        Assert.Null(SessionManager.StableLaunchWindow(7, [Top(8)]));
    }

    [Fact]
    public void AnInvalidHwndCountsAsClosed()
    {
        // 0x7FFFFFF0 is not a live window handle; 0 means "not cached" and is handled elsewhere.
        Assert.True(ScreenshotTool.WindowIsGone(0x7FFFFFF0));
        Assert.False(ScreenshotTool.WindowIsGone(0));
        Assert.Contains("has closed", ScreenshotTool.ClosedWindowMessage("w6"));
    }
}
