using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

public sealed partial class DragReleaseTests
{
    [Fact]
    public async Task ForegroundLossDuringAHeldDragReportsAnUnverifiedPossibleDrop()
    {
        var row = Element();
        var desktop = new FakeDesktop { FocusLostAfter = 3 };
        desktop.ElementBounds[row] = Row;
        desktop.Descriptions[row] = "TreeItem 'Source'";
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

        Assert.True(result.IsError != true);
        Assert.Equal("unverified", result.Outcome!.Status);
        Assert.Equal("completed", result.Outcome.Dispatch);
        Assert.Equal("drag-interrupted", result.Outcome.Failure!.Code);
        Assert.Contains("target-changed", result.Content[0].Text);
        Assert.Contains("Escape was not sent", result.Content[0].Text);
        Assert.Contains("whether the app dropped is not verified", result.Content[0].Text);
    }
}
