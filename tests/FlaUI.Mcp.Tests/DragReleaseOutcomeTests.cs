using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

public sealed partial class DragReleaseTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ADragThatBrokeOffFailsOnlyWhenTheButtonMayStillBeHeld(bool releaseFails)
    {
        var row = Element();
        var desktop = new FakeDesktop { FocusLostAfter = 3 };
        if (releaseFails)
        {
            // Move, press, one step; the lost foreground refuses the next step and the Escape; the fourth is the release.
            desktop.OnDispatch = count =>
            {
                if (count == 4)
                {
                    throw new InvalidOperationException("input blocked");
                }
            };
        }

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

        // A drag that let go of the button is unverified and a grant carries on; only a release that did not go
        // out, so the button may still be held, is failed, and a host's grant pauses on it.
        Assert.Equal(releaseFails, result.IsError == true);
        Assert.Equal(releaseFails ? "failed" : "unverified", result.Outcome!.Status);
        Assert.Equal(releaseFails ? "partial" : "completed", result.Outcome.Dispatch);
        Assert.Equal("drag-interrupted", result.Outcome.Failure!.Code);
        Assert.True(result.Outcome.Failure.ActionDispatched);
        Assert.Equal(releaseFails ? ["drag-interrupted"] : [], paused);
        Assert.Contains("target-changed", result.Content[0].Text);
        Assert.Contains("Escape was not sent", result.Content[0].Text);
        Assert.Contains(
            releaseFails ? "the release failed (input blocked)" : "the button was released",
            result.Content[0].Text
        );
        Assert.Contains("whether the app dropped is not verified", result.Content[0].Text);
    }
}
