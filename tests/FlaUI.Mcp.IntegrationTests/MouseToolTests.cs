using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit.Abstractions;
using static FlaUI.Mcp.IntegrationTests.PointerTestSupport;

namespace FlaUI.Mcp.IntegrationTests;

/// <summary>
/// windows_mouse (MouseTool and PointerActions) against the real test apps: where the pointer lands, what
/// a click, scroll or drag changes, and which requests are refused before any input. These files had no
/// integration coverage in the first pointer-integration mutation run.
/// </summary>
[Collection("TestApps")]
public class MouseToolTests(TestAppFixture fixture, ITestOutputHelper output)
{
    private MouseTool Pointer() => new(fixture.Elements, new PendingInvokeTracker(), fixture.Session);

    private Rectangle Origin(string handle) => fixture.Session.GetWindow(handle)!.BoundingRectangle;

    private static (Point Screen, Point Window, string Action) Landed(string reply)
    {
        using var json = JsonDocument.Parse(reply);
        var root = json.RootElement;
        Point Read(string name) =>
            new(root.GetProperty(name).GetProperty("x").GetInt32(), root.GetProperty(name).GetProperty("y").GetInt32());
        return (Read("screen"), Read("window"), root.GetProperty("action").GetString()!);
    }

    [Fact]
    public async Task MoveLandsOnTheRequestedWindowPixel()
    {
        // Kills MouseTool "move" case and "to"/"x"/"y" argument names, the window-relative arithmetic in the
        // reply, PointerPlan.ToScreen use, and PointerActions.Move (RequireHit, Send) statement removals.
        var reply = await fixture.CallTool(
            Pointer(),
            new
            {
                action = "move",
                handle = fixture.WpfHandle,
                to = new { x = 220, y = 160 },
            }
        );
        output.WriteLine(reply);
        var (screen, window, action) = Landed(reply);
        var origin = Origin(fixture.WpfHandle);
        Assert.Equal("move", action);
        Assert.Equal(new Point(220, 160), window);
        Assert.Equal(new Point(origin.X + 220, origin.Y + 160), screen);
        Assert.Equal(screen, FlaUI.Core.Input.Mouse.Position);
    }

    [Fact]
    public async Task HoverRestsForTheRequestedTime()
    {
        // Kills MouseTool HoverMs (durationMs name, ?? DefaultHoverMs) and PointerActions.Move's hoverMs > 0
        // boundary: an explicit 1500 ms hover must rest at least that long, the default at least 600 ms.
        var clock = Stopwatch.StartNew();
        await fixture.CallTool(
            Pointer(),
            new
            {
                action = "hover",
                handle = fixture.WpfHandle,
                to = new { x = 240, y = 170 },
                durationMs = 1500,
            }
        );
        Assert.True(clock.ElapsedMilliseconds >= 1500, $"Explicit hover took {clock.ElapsedMilliseconds} ms.");

        clock.Restart();
        await fixture.CallTool(
            Pointer(),
            new
            {
                action = "hover",
                handle = fixture.WpfHandle,
                to = new { x = 250, y = 170 },
            }
        );
        // The default hover is 600 ms. The upper bound is generous and only rules out the 5000 ms maximum.
        Assert.True(clock.ElapsedMilliseconds >= 600, $"Default hover took {clock.ElapsedMilliseconds} ms.");
        Assert.True(clock.ElapsedMilliseconds < 4500, $"Default hover took {clock.ElapsedMilliseconds} ms.");
    }

    [Fact]
    public async Task ClickByRefPressesTheElement()
    {
        // Kills MouseTool "click" case, ResolvePoint's ref branch (Geometry, ClickPoint.Choose) and
        // PointerActions.Click/ClickGuarded statement removals: the button must count one click.
        SelectTab(fixture, fixture.WinFormsHandle, "ButtonsTab");
        var (button, buttonRef) = Ref(fixture, fixture.WinFormsHandle, new(AutomationId: "ClickMeButton"));
        var before = ClickCount(fixture, fixture.WinFormsHandle);
        var reply = await fixture.CallTool(Pointer(), new { action = "click", @ref = buttonRef });
        var (screen, _, action) = Landed(reply);
        Assert.Equal("click", action);
        Assert.True(
            button.BoundingRectangle.Contains(screen),
            $"Clicked at {screen}, outside {button.BoundingRectangle}."
        );
        Assert.True(WaitFor(() => ClickCount(fixture, fixture.WinFormsHandle) == before + 1));
    }

    [Theory]
    [InlineData("click")]
    [InlineData("move")]
    public async Task PointOffTheWindowIsRefusedWithoutInput(string action)
    {
        // Kills PointerActions.RequireHit (under != root -> ==, throw removed, message emptied) for both the
        // click and the move path.
        var origin = Origin(fixture.WinFormsHandle);
        var parked = FlaUI.Core.Input.Mouse.Position;
        var error = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            Run(
                Pointer(),
                new
                {
                    action,
                    handle = fixture.WinFormsHandle,
                    to = new { x = -40, y = origin.Height / 2 },
                }
            )
        );
        output.WriteLine(error.Message);
        Assert.Contains("Point is not on the target window (off-window or covered)", error.Message);
        Assert.Equal(parked, FlaUI.Core.Input.Mouse.Position);
    }

    [Fact]
    public async Task ScrollMovesTheGridAndBack()
    {
        // Kills MouseTool "scroll" case and "ticks" name, and PointerActions.Scroll statement removals: wheel
        // ticks over the grid's rows must scroll it down, then back up (positive is up).
        var (grid, target) = GridRowsPoint();
        var scroll = grid.Patterns.Scroll.Pattern;
        try
        {
            await Wheel(target, -3);
            Assert.True(
                WaitFor(() => scroll.VerticalScrollPercent.Value > 0),
                $"Scrolling down did not move the grid ({scroll.VerticalScrollPercent.Value}%)."
            );
            await Wheel(target, 3);
            Assert.True(
                WaitFor(() => scroll.VerticalScrollPercent.Value < 0.5),
                $"Scrolling up did not return ({scroll.VerticalScrollPercent.Value}%)."
            );
        }
        finally
        {
            scroll.SetScrollPercent(-1, 0);
            SelectTab(fixture, fixture.WpfHandle, "ButtonsTab");
        }
    }

    /// <summary>
    /// Show the grid at the top and return a window pixel over its data rows: below the header row and left of
    /// the scroll bar, so the wheel lands on a cell whatever the grid reports as its clickable point.
    /// </summary>
    private (FlaUI.Core.AutomationElements.AutomationElement Grid, Point Target) GridRowsPoint()
    {
        SelectTab(fixture, fixture.WpfHandle, "GridTab");
        var grid = Resolve(fixture, fixture.WpfHandle, new(AutomationId: "TestDataGrid"));
        Assert.True(WaitFor(() => !grid.Properties.IsOffscreen.Value && !grid.BoundingRectangle.IsEmpty));
        var scroll = grid.Patterns.Scroll.Pattern;
        scroll.SetScrollPercent(-1, 0);
        Assert.True(WaitFor(() => scroll.VerticalScrollPercent.Value < 0.5));
        var bounds = grid.BoundingRectangle;
        var origin = Origin(fixture.WpfHandle);
        var target = new Point(bounds.X + bounds.Width / 3 - origin.X, bounds.Y + bounds.Height * 2 / 3 - origin.Y);
        var clickable = grid.TryGetClickablePoint(out var reported) ? reported.ToString() : "none";
        output.WriteLine($"Grid {bounds}, reported clickable point {clickable}, wheel at window {target}.");
        return (grid, target);
    }

    /// <summary>
    /// Move the pointer onto the window a little away from the target first, so the wheel's own move to the
    /// target is a real move and the app's hover state follows it, then send the ticks.
    /// </summary>
    private async Task Wheel(Point target, int ticks)
    {
        await fixture.CallTool(
            Pointer(),
            new
            {
                action = "move",
                handle = fixture.WpfHandle,
                to = new { x = target.X + 15, y = target.Y - 15 },
            }
        );
        var reply = await fixture.CallTool(
            Pointer(),
            new
            {
                action = "scroll",
                handle = fixture.WpfHandle,
                to = new { x = target.X, y = target.Y },
                ticks,
            }
        );
        var screen = Landed(reply).Screen;
        output.WriteLine(
            $"scroll {ticks}: {reply}; foreground {Win32Desktop.GetForegroundWindow()}, "
                + $"under pointer {Win32Desktop.WindowAt(screen)}, WPF {fixture.Session.GetWindowHwnd(fixture.WpfHandle)}"
        );
    }

    [Theory]
    [InlineData(50)]
    [InlineData(-50)]
    public async Task ScrollAcceptsTheLimitTicks(int ticks)
    {
        // Kills MouseTool.Ticks boundaries (< -50 -> <= -50, > 50 -> >= 50).
        SelectTab(fixture, fixture.WpfHandle, "GridTab");
        var (grid, gridRef) = Ref(fixture, fixture.WpfHandle, new(AutomationId: "TestDataGrid"));
        try
        {
            var reply = await fixture.CallTool(
                Pointer(),
                new
                {
                    action = "scroll",
                    @ref = gridRef,
                    ticks,
                }
            );
            Assert.Equal("scroll", Landed(reply).Action);
        }
        finally
        {
            grid.Patterns.Scroll.Pattern.SetScrollPercent(-1, 0);
            SelectTab(fixture, fixture.WpfHandle, "ButtonsTab");
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    [InlineData(-51)]
    [InlineData(null)]
    public async Task ScrollOutsideTheLimitsIsRefusedBeforeInput(int? ticks)
    {
        // Kills MouseTool.Ticks (is 0 removed, ?? throw removed, boundaries widened) and the order that
        // checks ticks before any focus change.
        Foreground(fixture.Session.GetWindowHwnd(fixture.WinFormsHandle));
        var context = Counting();
        try
        {
            object args = ticks is { } t
                ? new
                {
                    action = "scroll",
                    handle = fixture.WpfHandle,
                    to = new { x = 200, y = 200 },
                    ticks = t,
                }
                : new
                {
                    action = "scroll",
                    handle = fixture.WpfHandle,
                    to = new { x = 200, y = 200 },
                };
            await Assert.ThrowsAsync<ArgumentException>(() => Run(Pointer(), args));
            Assert.Equal(0, context.InputsSent);
            Assert.Equal(fixture.Session.GetWindowHwnd(fixture.WinFormsHandle), Win32Desktop.GetForegroundWindow());
        }
        finally
        {
            StopCounting();
        }
    }

    [Fact]
    public async Task DragBetweenPointsMovesTheSliderDown()
    {
        // Kills MouseTool "drag" case, "from" name, DragFromArguments' point branch, DropPoint's `to` branch and
        // PointerActions.Drag statement removals (press, path moves, release).
        var slider = PrioritySlider(out _);
        var bounds = slider.BoundingRectangle;
        var origin = Origin(fixture.WpfHandle);
        var center = new Point(bounds.X + bounds.Width / 2 - origin.X, bounds.Y + bounds.Height / 2 - origin.Y);
        var left = new Point(bounds.X + 2 - origin.X, center.Y);
        try
        {
            var reply = await fixture.CallTool(
                Pointer(),
                new
                {
                    action = "drag",
                    handle = fixture.WpfHandle,
                    from = new { x = center.X, y = center.Y },
                    to = new { x = left.X, y = left.Y },
                }
            );
            output.WriteLine(reply);
            Assert.Equal("drag", Landed(reply).Action);
            Assert.True(
                WaitFor(() => slider.Patterns.RangeValue.Pattern.Value.Value < 5),
                "The slider did not move left."
            );
        }
        finally
        {
            slider.Patterns.RangeValue.Pattern.SetValue(5);
            SelectTab(fixture, fixture.WpfHandle, "ButtonsTab");
        }
    }

    [Fact]
    public async Task DragFromRefToAnotherRefsEdgeMovesTheSliderUp()
    {
        // Kills MouseTool "toRef"/"toEdge" names, DropPoint's toRef branch and EdgeOf: releasing on the
        // slider's right edge must raise the value; a centre drop (toEdge ignored) would leave it at 5.
        var slider = PrioritySlider(out var sliderRef);
        try
        {
            await fixture.CallTool(
                Pointer(),
                new
                {
                    action = "drag",
                    @ref = sliderRef,
                    toRef = sliderRef,
                    toEdge = "right",
                }
            );
            Assert.True(
                WaitFor(() => slider.Patterns.RangeValue.Pattern.Value.Value > 5),
                "The slider did not move right."
            );
        }
        finally
        {
            slider.Patterns.RangeValue.Pattern.SetValue(5);
            SelectTab(fixture, fixture.WpfHandle, "ButtonsTab");
        }
    }

    [Fact]
    public async Task ResizeDragMakesTheColumnTheRequestedWidth()
    {
        // Kills MouseTool "fromEdge"/"resizeTo" names, the resizeTo == null branch, fromEdge ?? "right" and
        // PointerPlan.ResizeTarget use: one drag must leave the column header at the requested width.
        SelectTab(fixture, fixture.WpfHandle, "GridTab");
        var (header, headerRef) = Ref(
            fixture,
            fixture.WpfHandle,
            new(Name: "ID", ControlType: "HeaderItem"),
            new(AutomationId: "TestDataGrid")
        );
        var original = header.BoundingRectangle.Width;
        var wanted = original + 40;
        try
        {
            await fixture.CallTool(
                Pointer(),
                new
                {
                    action = "drag",
                    @ref = headerRef,
                    fromEdge = "right",
                    resizeTo = wanted,
                }
            );
            Assert.True(
                WaitFor(() => Math.Abs(header.BoundingRectangle.Width - wanted) <= 3),
                $"Header is {header.BoundingRectangle.Width} px, wanted {wanted}."
            );
        }
        finally
        {
            await fixture.CallTool(
                Pointer(),
                new
                {
                    action = "drag",
                    @ref = headerRef,
                    fromEdge = "right",
                    resizeTo = original,
                }
            );
            SelectTab(fixture, fixture.WpfHandle, "ButtonsTab");
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public async Task DragWithABadStepCountIsRefusedBeforeAnyFocusChange(int steps)
    {
        // Kills PointerActions.Drag's first DragPath check (Statement removal: the guard would activate the
        // target first) and MouseTool's "steps" name (?? DefaultDragSteps would accept it).
        Foreground(fixture.Session.GetWindowHwnd(fixture.WinFormsHandle));
        var context = Counting();
        try
        {
            await Assert.ThrowsAsync<ArgumentException>(() =>
                Run(
                    Pointer(),
                    new
                    {
                        action = "drag",
                        handle = fixture.WpfHandle,
                        from = new { x = 200, y = 200 },
                        to = new { x = 210, y = 200 },
                        steps,
                    }
                )
            );
            Assert.Equal(0, context.InputsSent);
            Assert.Equal(fixture.Session.GetWindowHwnd(fixture.WinFormsHandle), Win32Desktop.GetForegroundWindow());
        }
        finally
        {
            StopCounting();
        }
    }

    [Fact]
    public async Task ParkMovesThePointerOffTheWindow()
    {
        // Kills MouseTool "park" case: the pointer must end outside the window, on the window's own monitor.
        var reply = await fixture.CallTool(Pointer(), new { action = "park", handle = fixture.WpfHandle });
        var (screen, _, action) = Landed(reply);
        var window = Origin(fixture.WpfHandle);
        Assert.Equal("park", action);
        Assert.False(window.Contains(screen), $"Parked at {screen}, inside the window.");
        var monitor = System.Windows.Forms.Screen.FromRectangle(window).Bounds;
        Assert.True(monitor.Contains(screen), $"Parked at {screen}, off the window's monitor {monitor}.");
        Assert.Equal(screen, FlaUI.Core.Input.Mouse.Position);
    }

    [Fact]
    public async Task UnknownActionAndMissingTargetAreRefused()
    {
        // Kills MouseTool's action-name check (default case message) and "handle or ref required".
        var unknown = await Assert.ThrowsAsync<ArgumentException>(() =>
            Run(Pointer(), new { action = "wiggle", handle = fixture.WpfHandle })
        );
        Assert.Equal("action must be park, move, hover, scroll, click or drag", unknown.Message);
        var missing = await Run(Pointer(), new { action = "move", to = new { x = 1, y = 1 } });
        Assert.True(missing.IsError);
        Assert.Equal("handle or ref required", Text(missing));
    }

    private FlaUI.Core.AutomationElements.AutomationElement PrioritySlider(out string sliderRef)
    {
        SelectTab(fixture, fixture.WpfHandle, "FormsTab");
        var (slider, reference) = Ref(fixture, fixture.WpfHandle, new(AutomationId: "PrioritySlider"));
        slider.Patterns.RangeValue.Pattern.SetValue(5);
        Assert.True(WaitFor(() => Math.Abs(slider.Patterns.RangeValue.Pattern.Value.Value - 5) < 0.01));
        sliderRef = reference;
        return slider;
    }
}
