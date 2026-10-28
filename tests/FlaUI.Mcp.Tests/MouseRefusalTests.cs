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
/// windows_mouse reports a refusal the way windows_click does: a failed result that says no input was sent,
/// never an exception. A direct caller and the registry then see the same reply.
/// </summary>
[Collection(InputLeaseCollection.Name)]
public sealed class MouseRefusalTests
{
    private static readonly Point From = new(10, 10);
    private static readonly Point To = new(50, 10);

    private static JsonElement Args(object value) => JsonSerializer.SerializeToElement(value);

    private static AutomationElement Element() =>
        (AutomationElement)RuntimeHelpers.GetUninitializedObject(typeof(AutomationElement));

    private static (ElementRegistry Refs, SessionManager Sessions, string Handle) Window()
    {
        var sessions = new SessionManager(null, ProcessPolicy.AllowAll);
        // No native handle to attach to: the window's origin then comes from the desktop seam, not UI Automation.
        var handle = sessions.PublishWindow(null, 0, FakeDesktop.TargetPid, FakeDesktop.Target);
        return (new ElementRegistry(), sessions, handle);
    }

    private static Task<McpToolResult> Run(MouseTool tool, object arguments)
    {
        var registry = new ToolRegistry();
        registry.RegisterTool(tool);
        return registry.ExecuteToolAsync(tool.Name, Args(arguments));
    }

    [Fact]
    public async Task ADragRefusedByTheHitTestIsAFailedResultWhenCalledDirectly()
    {
        // The hit test's refusal (click-point-covered, as a covered drop point raises) escaped MouseTool as an
        // exception, while windows_click returned it as a failed result.
        var overlay = Element();
        var desktop = new FakeDesktop { HitTest = _ => overlay };
        desktop.Descriptions[overlay] = "Text 'Overlay'";
        using var installed = desktop.Install();
        var (refs, sessions, handle) = Window();
        using var _ = sessions;
        var forbidden = refs.Register(handle, overlay);

        var result = await new MouseTool(refs, new PendingInvokeTracker(), sessions).ExecuteAsync(
            Args(
                new
                {
                    action = "drag",
                    handle,
                    from = new { x = From.X, y = From.Y },
                    to = new { x = To.X, y = To.Y },
                    fromMustNotHit = new[] { forbidden },
                }
            )
        );

        Assert.True(result.IsError);
        Assert.StartsWith("Failed to drag: The drag start (", result.Content[0].Text);
        Assert.Contains("is on Text 'Overlay', which must not be pressed; no input sent.", result.Content[0].Text);
        Assert.Equal(0, desktop.Dispatched);
    }

    [Fact]
    public async Task ADragRefusedByTheHitTestCarriesTheRefusalThroughTheRegistry()
    {
        var overlay = Element();
        var desktop = new FakeDesktop { HitTest = _ => overlay };
        using var installed = desktop.Install();
        var (refs, sessions, handle) = Window();
        using var _ = sessions;
        var forbidden = refs.Register(handle, overlay);

        var result = await Run(
            new MouseTool(refs, new PendingInvokeTracker(), sessions),
            new
            {
                action = "drag",
                handle,
                from = new { x = From.X, y = From.Y },
                to = new { x = To.X, y = To.Y },
                fromMustNotHit = new[] { forbidden },
            }
        );

        Assert.True(result.IsError);
        Assert.Equal("failed", result.Outcome!.Status);
        Assert.Equal("not-dispatched", result.Outcome.Dispatch);
        Assert.Equal(0, result.Outcome.InputsSent);
        Assert.Equal("click-point-covered", result.Outcome.Failure!.Code);
        Assert.Equal(0, desktop.Dispatched);
    }

    [Theory]
    [InlineData("drag")]
    [InlineData("click")]
    [InlineData("move")]
    [InlineData("scroll")]
    public async Task APointUnderAnotherWindowIsAFailedResult(string action)
    {
        // The drop (or click, move, scroll) point lies under another window: refused by the window hit test.
        var desktop = new FakeDesktop();
        var covered = PointerPlan.ToScreen(desktop.Bounds, To);
        desktop.WindowUnder = point => point == covered ? FakeDesktop.OtherHwnd : FakeDesktop.TargetHwnd;
        using var installed = desktop.Install();
        var (refs, sessions, handle) = Window();
        using var _ = sessions;

        var result = await Run(
            new MouseTool(refs, new PendingInvokeTracker(), sessions),
            new
            {
                action,
                handle,
                from = new { x = From.X, y = From.Y },
                to = new { x = To.X, y = To.Y },
                ticks = 1,
            }
        );

        Assert.True(result.IsError);
        Assert.Equal(
            $"Failed to {action}: Point is not on the target window (off-window or covered); no input sent.",
            result.Content[0].Text
        );
        Assert.Equal("not-dispatched", result.Outcome!.Dispatch);
        Assert.Equal(0, desktop.Dispatched);
    }

    [Fact]
    public async Task ARawDragRefusesWhenTheSameWindowMovesBeforeThePress()
    {
        var desktop = new FakeDesktop();
        var original = desktop.Bounds;
        var hitTests = 0;
        desktop.WindowUnder = _ =>
        {
            if (++hitTests == 1)
            {
                desktop.Bounds = new Rectangle(original.X + 8, original.Y, original.Width, original.Height);
            }

            return FakeDesktop.TargetHwnd;
        };
        using var installed = desktop.Install();
        var (refs, sessions, handle) = Window();
        using var _ = sessions;

        var result = await Run(
            new MouseTool(refs, new PendingInvokeTracker(), sessions),
            new
            {
                action = "drag",
                handle,
                from = new { x = From.X, y = From.Y },
                to = new { x = To.X, y = To.Y },
            }
        );

        Assert.True(result.IsError);
        Assert.Equal(RefusalCodes.TargetChanged, result.Outcome!.Failure?.Code);
        Assert.Equal("not-dispatched", result.Outcome.Dispatch);
        Assert.Equal(0, desktop.Dispatched);
    }

    [Fact]
    public async Task AnArgumentErrorStillThrowsForTheRegistryToType()
    {
        // Not a refusal: the registry reports it as "Error: ...", as for every tool.
        using var installed = new FakeDesktop().Install();
        var (refs, sessions, handle) = Window();
        using var _ = sessions;

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new MouseTool(refs, new PendingInvokeTracker(), sessions).ExecuteAsync(
                Args(new { action = "wiggle", handle })
            )
        );
    }
}
