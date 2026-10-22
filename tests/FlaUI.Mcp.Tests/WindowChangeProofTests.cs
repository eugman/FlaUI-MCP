using System.Text.Json;
using FlaUI.Mcp;
using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// Window placement and state report what they observed: a placement that landed elsewhere is a failure with
/// the deltas, and a state request that matches the current state is a no-op that sends nothing.
/// </summary>
[Collection(InputLeaseCollection.Name)]
public sealed class WindowChangeProofTests
{
    private static JsonElement Args(object value) => JsonSerializer.SerializeToElement(value);

    private static async Task<McpToolResult> Run(ITool tool, object arguments)
    {
        var registry = new ToolRegistry();
        registry.RegisterTool(tool);
        return await registry.ExecuteToolAsync(tool.Name, Args(arguments));
    }

    private static object Placement()
    {
        var area = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
        return new
        {
            x = area.X + 10,
            y = area.Y + 10,
            width = 400,
            height = 300,
        };
    }

    private static SessionManager Sessions(out string handle)
    {
        var sessions = new SessionManager(null, ProcessPolicy.AllowAll);
        handle = sessions.PublishWindow(null, FakeDesktop.TargetHwnd, FakeDesktop.TargetPid, FakeDesktop.Target);
        return sessions;
    }

    [Fact]
    public async Task APlacementThatLandsWhereRequestedIsVerified()
    {
        using var installed = new FakeDesktop().Install();
        using var sessions = Sessions(out var handle);

        var result = await Run(
            new WindowPlacementTool(new ElementRegistry(), new PendingInvokeTracker(), sessions),
            new { handle, placement = Placement() }
        );

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("verified", result.Outcome!.Verification);
    }

    [Fact]
    public async Task APlacementTheSystemClampedIsAFailureWithItsDeltas()
    {
        var desktop = new FakeDesktop { PlaceAs = b => b with { Width = b.Width + 40, Y = b.Y + 2 } };
        using var installed = desktop.Install();
        using var sessions = Sessions(out var handle);

        var result = await Run(
            new WindowPlacementTool(new ElementRegistry(), new PendingInvokeTracker(), sessions),
            new { handle, placement = Placement() }
        );

        Assert.True(result.IsError);
        Assert.Equal("completed", result.Outcome!.Dispatch);
        Assert.Equal("mismatch", result.Outcome.Verification);
        Assert.Equal(1, result.Outcome.InputsSent);
        var text = result.Content[0].Text!;
        Assert.StartsWith("Window did not land where requested: ", text);
        var json = JsonDocument.Parse(text["Window did not land where requested: ".Length..]).RootElement;
        var delta = json.GetProperty("delta");
        Assert.Equal(0, delta.GetProperty("x").GetInt32());
        Assert.Equal(2, delta.GetProperty("y").GetInt32());
        Assert.Equal(40, delta.GetProperty("width").GetInt32());
        Assert.Equal(0, delta.GetProperty("height").GetInt32());
    }

    [Fact]
    public async Task AStateTheWindowIsAlreadyInSendsNothing()
    {
        var desktop = new FakeDesktop { State = WindowStates.Maximized };
        using var installed = desktop.Install();
        using var sessions = Sessions(out var handle);

        var result = await Run(
            new WindowStateTool(new ElementRegistry(), new PendingInvokeTracker(), sessions),
            new { handle, state = "maximize" }
        );

        Assert.NotEqual(true, result.IsError);
        Assert.Empty(desktop.StateRequests);
        Assert.Equal(0, result.Outcome!.InputsSent);
        Assert.Equal("no-op", result.Outcome.Verification);
    }
}
