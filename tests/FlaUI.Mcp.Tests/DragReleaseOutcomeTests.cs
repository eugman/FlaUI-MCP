using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

public sealed partial class DragReleaseTests
{
    [Fact]
    public async Task ForegroundLossDuringAHeldDragFailsSoAGrantPauses()
    {
        var row = Element();
        var desktop = new FakeDesktop { FocusLostAfter = 3 };
        desktop.ElementBounds[row] = Row;
        desktop.Descriptions[row] = "TreeItem 'Source'";
        using var installed = desktop.Install();
        var (refs, sessions, handle) = Window();
        using var _ = sessions;
        var reference = refs.Register(handle, row);

        var tool = new MouseTool(refs, new PendingInvokeTracker(), sessions);
        var paused = new List<string>();
        var registry = new ToolRegistry { FailedAfterInput = (name, outcome) => paused.Add(outcome.Failure!.Code) };
        registry.RegisterTool(tool);

        var result = await registry.ExecuteToolAsync(
            tool.Name,
            Args(
                new
                {
                    action = "drag",
                    @ref = reference,
                    toRef = reference,
                }
            )
        );

        // The button was down when the drag broke off: failed, not unverified, so a host's grant pauses on it.
        Assert.True(result.IsError);
        Assert.Equal("failed", result.Outcome!.Status);
        Assert.Equal("partial", result.Outcome.Dispatch);
        Assert.Equal("drag-interrupted", result.Outcome.Failure!.Code);
        Assert.True(result.Outcome.Failure.ActionDispatched);
        Assert.Equal(["drag-interrupted"], paused);
        Assert.Contains("target-changed", result.Content[0].Text);
        Assert.Contains("Escape was not sent", result.Content[0].Text);
        Assert.Contains("whether the app dropped is not verified", result.Content[0].Text);
    }
}
