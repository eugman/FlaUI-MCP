using System.Drawing;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Mcp;
using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>Raw screenshot coordinates are guarded by an element ref before input and at drag release.</summary>
[Collection(InputLeaseCollection.Name)]
public sealed class RawDragGuardTests
{
    private static readonly Rectangle StartBounds = new(105, 105, 20, 20);
    private static readonly Rectangle DropBounds = new(145, 105, 20, 20);

    private static AutomationElement Element() =>
        (AutomationElement)RuntimeHelpers.GetUninitializedObject(typeof(AutomationElement));

    private static ClickGeometry GuardGeometry(
        FakeDesktop desktop,
        AutomationElement element,
        AutomationElement drop,
        bool moveDrop,
        bool dropOffscreen
    ) =>
        new(
            element == drop && dropOffscreen && desktop.Dispatched > 2,
            null,
            element == drop && moveDrop && desktop.Dispatched > 2
                ? new Rectangle(145, 145, 20, 20)
                : desktop.ElementBounds[element],
            []
        );

    private sealed record GuardScenario
    {
        public bool WrongStart { get; init; }
        public bool WrongDrop { get; init; }
        public bool MoveDrop { get; init; }
        public bool HoverCoversStart { get; init; }
        public bool DropOffscreen { get; init; }
        public bool WrongWindow { get; init; }
        public bool WrongStartWindow { get; init; }
        public bool RefDrop { get; init; }
        public bool OmitStart { get; init; }
        public bool OmitDrop { get; init; }
        public bool RefStart { get; init; }
        public bool InitialDropOffscreen { get; init; }
        public bool DropGeometryFails { get; init; }
        public string? Incompatible { get; init; }
    }

    private static void ConfigureGeometry(FakeDesktop desktop, GuardScenario scenario, AutomationElement drop)
    {
        if (scenario.MoveDrop || scenario.DropOffscreen)
        {
            desktop.OnGeometry = element =>
                GuardGeometry(desktop, element, drop, scenario.MoveDrop, scenario.DropOffscreen);
        }

        if (scenario.DropGeometryFails)
        {
            desktop.OnGeometry = element =>
                element == drop && desktop.Dispatched > 2
                    ? throw new TimeoutException("provider blocked")
                    : new ClickGeometry(false, null, desktop.ElementBounds[element], []);
        }
    }

    private static string? SourceRef(GuardScenario scenario, string startRef) =>
        scenario.RefStart || scenario.Incompatible is "fromEdge" or "resizeTo" or "toResize" ? startRef : null;

    private static object DragArgs(GuardScenario scenario, string handle, string startRef, string dropRef) =>
        new
        {
            action = "drag",
            handle,
            @ref = SourceRef(scenario, startRef),
            from = scenario.OmitStart || scenario.RefStart ? null : new { x = 10, y = 10 },
            fromEdge = scenario.RefStart || scenario.Incompatible == "fromEdge" ? "right" : null,
            resizeTo = scenario.Incompatible is "resizeTo" or "toResize" ? 250 : (int?)null,
            to = scenario.OmitDrop || scenario.RefDrop ? null : new { x = 50, y = 10 },
            toRef = scenario.RefDrop || scenario.Incompatible == "toRef" ? dropRef : null,
            fromMustHit = scenario.RefStart || scenario.Incompatible == "toResize" ? null : startRef,
            toMustHit = scenario.RefDrop ? null : dropRef,
        };

    private static async Task<McpToolResult> Run(FakeDesktop desktop, GuardScenario scenario)
    {
        using var sessions = new SessionManager(null, ProcessPolicy.AllowAll);
        var handle = sessions.PublishWindow(null, 0, FakeDesktop.TargetPid, FakeDesktop.Target);
        var refs = new ElementRegistry();
        var start = Element();
        var drop = Element();
        var covering = Element();
        desktop.ElementBounds[start] = StartBounds;
        desktop.ElementBounds[drop] = DropBounds;
        if (scenario.InitialDropOffscreen)
        {
            desktop.Offscreen.Add(drop);
        }

        ConfigureGeometry(desktop, scenario, drop);

        desktop.Descriptions[drop] = "Pane 'Drop'";
        desktop.Descriptions[covering] = "Pane 'Covering'";
        desktop.HitTest = point =>
        {
            if (point.X < 130)
            {
                return scenario.WrongStart || (scenario.HoverCoversStart && desktop.Dispatched >= 1) ? covering : start;
            }

            return scenario.WrongDrop ? covering : drop;
        };
        var startRef = refs.Register(scenario.WrongStartWindow ? "other" : handle, start);
        var dropRef = refs.Register(scenario.WrongWindow ? "other" : handle, drop);
        var registry = new ToolRegistry();
        var tool = new MouseTool(refs, new PendingInvokeTracker(), sessions);
        registry.RegisterTool(tool);
        return await registry.ExecuteToolAsync(
            tool.Name,
            JsonSerializer.SerializeToElement(DragArgs(scenario, handle, startRef, dropRef))
        );
    }

    [Fact]
    public async Task RawStartCoveredByAnotherElementRefusesBeforeInput()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        var result = await Run(desktop, new() { WrongStart = true });
        Assert.Equal("click-point-covered", result.Outcome!.Failure!.Code);
        Assert.Equal("not-dispatched", result.Outcome.Dispatch);
        Assert.Equal(0, desktop.Dispatched);
    }

    [Fact]
    public async Task RawDropCoveredByAnotherElementRefusesBeforeInput()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        var result = await Run(desktop, new() { WrongDrop = true });
        Assert.Equal("click-point-covered", result.Outcome!.Failure!.Code);
        Assert.Equal("not-dispatched", result.Outcome.Dispatch);
        Assert.Equal(0, desktop.Dispatched);
    }

    [Fact]
    public async Task RawDropTargetMovingDuringDragIsReportedAsUnverified()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        var result = await Run(desktop, new() { MoveDrop = true });
        Assert.Equal("drop-target-moved", result.Outcome!.Failure!.Code);
        Assert.Equal("unverified", result.Outcome.Status);
        Assert.Equal(16, desktop.Dispatched);
    }

    [Fact]
    public async Task GuardFromAnotherWindowRefusesBeforeInput()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        var result = await Run(desktop, new() { WrongWindow = true });
        Assert.Equal("invalid-arguments", result.Outcome!.Failure!.Code);
        Assert.Equal("not-dispatched", result.Outcome.Dispatch);
        Assert.Contains("belongs to window", result.Content[0].Text);
        Assert.Equal(0, desktop.Dispatched);
    }

    [Fact]
    public async Task WrongWindowDropGuardRefusesBeforeRefStartCanScroll()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        var result = await Run(desktop, new() { RefStart = true, WrongWindow = true });
        Assert.Equal("invalid-arguments", result.Outcome!.Failure!.Code);
        Assert.Equal("not-dispatched", result.Outcome.Dispatch);
        Assert.Equal(0, desktop.ScrollRequests);
        Assert.Equal(0, desktop.Dispatched);
    }

    [Fact]
    public async Task WrongWindowStartGuardRefusesBeforeRefDropCanScroll()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        var result = await Run(desktop, new() { WrongStartWindow = true, RefDrop = true });
        Assert.Equal("invalid-arguments", result.Outcome!.Failure!.Code);
        Assert.Equal("not-dispatched", result.Outcome.Dispatch);
        Assert.Equal(0, desktop.ScrollRequests);
        Assert.Equal(0, desktop.Dispatched);
    }

    [Fact]
    public async Task HoverOverlayAtStartRefusesBeforeMouseDown()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        var result = await Run(desktop, new() { HoverCoversStart = true });
        Assert.Equal("click-point-covered", result.Outcome!.Failure!.Code);
        Assert.True(result.Outcome.Failure.ActionDispatched);
        Assert.Contains("the pointer moved to the start point first", result.Content[0].Text);
        Assert.DoesNotContain("; no input sent", result.Content[0].Text);
        Assert.Equal(1, desktop.Dispatched);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task AGuardWithoutItsRawPointIsTypedInvalidArguments(bool omitStart, bool omitDrop)
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        var result = await Run(desktop, new() { OmitStart = omitStart, OmitDrop = omitDrop });
        Assert.Equal("invalid-arguments", result.Outcome!.Failure!.Code);
        Assert.Equal("not-dispatched", result.Outcome.Dispatch);
        Assert.Equal(0, desktop.Dispatched);
    }

    [Theory]
    [InlineData("fromEdge")]
    [InlineData("resizeTo")]
    [InlineData("toRef")]
    [InlineData("toResize")]
    public async Task IncompatibleGuardTargetingIsTypedBeforeInput(string incompatible)
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        var result = await Run(desktop, new() { Incompatible = incompatible });
        Assert.Equal("invalid-arguments", result.Outcome!.Failure!.Code);
        Assert.Equal("not-dispatched", result.Outcome.Dispatch);
        Assert.Equal(0, desktop.ScrollRequests);
        Assert.Equal(0, desktop.Dispatched);
    }

    [Fact]
    public async Task InitiallyHiddenGuardedDropRefusesBeforeInput()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        var result = await Run(desktop, new() { InitialDropOffscreen = true });
        Assert.Equal("click-point-unavailable", result.Outcome!.Failure!.Code);
        Assert.Equal("not-dispatched", result.Outcome.Dispatch);
        Assert.Equal(0, desktop.Dispatched);
    }

    [Fact]
    public async Task OffscreenGuardedDropIsReportedAsMoved()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        var result = await Run(desktop, new() { DropOffscreen = true });
        Assert.Equal("drop-target-moved", result.Outcome!.Failure!.Code);
        Assert.Equal("unverified", result.Outcome.Status);
        Assert.Contains("the guarded point is no longer visible", result.Content[0].Text);
        Assert.DoesNotContain("no input sent", result.Content[0].Text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(16, desktop.Dispatched);
    }

    [Fact]
    public async Task UnreadableHeldDropReportsItsProviderFailure()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        var result = await Run(desktop, new() { DropGeometryFails = true });
        Assert.Equal("drop-target-moved", result.Outcome!.Failure!.Code);
        Assert.Contains("provider blocked", result.Content[0].Text);
        Assert.DoesNotContain("no input sent", result.Content[0].Text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(16, desktop.Dispatched);
    }

    [Fact]
    public async Task RawGuardedDragReleasesOnRequiredRefs()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        var result = await Run(desktop, new());
        Assert.False(result.IsError == true, result.Content[0].Text);
        Assert.Equal(15, desktop.Dispatched);
        using var reply = JsonDocument.Parse(result.Content[0].Text!);
        Assert.Equal("Pane 'Drop'", reply.RootElement.GetProperty("droppedOn").GetString());
        var from = reply.RootElement.GetProperty("fromPoint");
        Assert.Equal(110, from.GetProperty("screen").GetProperty("x").GetInt32());
        Assert.Equal(110, from.GetProperty("screen").GetProperty("y").GetInt32());
        Assert.Equal(10, from.GetProperty("window").GetProperty("x").GetInt32());
        Assert.Equal(10, from.GetProperty("window").GetProperty("y").GetInt32());
        Assert.Equal(0, desktop.ScrollRequests);
    }
}
