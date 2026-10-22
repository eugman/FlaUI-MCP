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

    private static async Task<McpToolResult> Run(
        FakeDesktop desktop,
        bool wrongStart,
        bool wrongDrop,
        bool moveDrop = false
    )
    {
        using var sessions = new SessionManager(null, ProcessPolicy.AllowAll);
        var handle = sessions.PublishWindow(null, 0, FakeDesktop.TargetPid, FakeDesktop.Target);
        var refs = new ElementRegistry();
        var start = Element();
        var drop = Element();
        var covering = Element();
        desktop.ElementBounds[start] = StartBounds;
        desktop.ElementBounds[drop] = DropBounds;
        if (moveDrop)
        {
            desktop.OnGeometry = element => new ClickGeometry(
                false,
                null,
                element == drop && desktop.Dispatched > 2
                    ? new Rectangle(145, 145, 20, 20)
                    : desktop.ElementBounds[element],
                []
            );
        }

        desktop.Descriptions[covering] = "Pane 'Covering'";
        desktop.HitTest = point =>
        {
            if (point.X < 130)
            {
                return wrongStart ? covering : start;
            }

            return wrongDrop ? covering : drop;
        };
        var startRef = refs.Register(handle, start);
        var dropRef = refs.Register(handle, drop);
        var registry = new ToolRegistry();
        var tool = new MouseTool(refs, new PendingInvokeTracker(), sessions);
        registry.RegisterTool(tool);
        return await registry.ExecuteToolAsync(
            tool.Name,
            JsonSerializer.SerializeToElement(
                new
                {
                    action = "drag",
                    handle,
                    from = new { x = 10, y = 10 },
                    to = new { x = 50, y = 10 },
                    fromMustHit = startRef,
                    toMustHit = dropRef,
                }
            )
        );
    }

    [Fact]
    public async Task RawStartCoveredByAnotherElementRefusesBeforeInput()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        var result = await Run(desktop, wrongStart: true, wrongDrop: false);
        Assert.Equal("click-point-covered", result.Outcome!.Failure!.Code);
        Assert.Equal("not-dispatched", result.Outcome.Dispatch);
        Assert.Equal(0, desktop.Dispatched);
    }

    [Fact]
    public async Task RawDropCoveredByAnotherElementRefusesBeforeInput()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        var result = await Run(desktop, wrongStart: false, wrongDrop: true);
        Assert.Equal("click-point-covered", result.Outcome!.Failure!.Code);
        Assert.Equal("not-dispatched", result.Outcome.Dispatch);
        Assert.Equal(0, desktop.Dispatched);
    }

    [Fact]
    public async Task RawDropTargetMovingDuringDragIsReportedAsUnverified()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        var result = await Run(desktop, wrongStart: false, wrongDrop: false, moveDrop: true);
        Assert.Equal("drop-target-moved", result.Outcome!.Failure!.Code);
        Assert.Equal("unverified", result.Outcome.Status);
        Assert.Equal(16, desktop.Dispatched);
    }

    [Fact]
    public async Task RawGuardedDragReleasesOnRequiredRefs()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        var result = await Run(desktop, wrongStart: false, wrongDrop: false);
        Assert.False(result.IsError == true, result.Content[0].Text);
        Assert.Equal(15, desktop.Dispatched);
        Assert.Equal(0, desktop.ScrollRequests);
    }
}
