using System.Text.Json;
using FlaUI.Mcp;
using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// An argument check a tool fails before any input is a typed refusal, invalid-arguments, not a bare error
/// text: a client reads failure.code and actionDispatched false instead of parsing the message.
/// </summary>
[Collection(InputLeaseCollection.Name)]
public sealed class InvalidArgumentsTests
{
    private static JsonElement Args(object value) => JsonSerializer.SerializeToElement(value);

    private static async Task<McpToolResult> Run(ToolBase tool, object arguments)
    {
        var registry = new ToolRegistry();
        registry.RegisterTool(tool);
        return await registry.ExecuteToolAsync(tool.Name, Args(arguments));
    }

    private static void AssertInvalid(McpToolResult result, string message)
    {
        Assert.True(result.IsError);
        Assert.Equal(message, result.Content[0].Text);
        Assert.Equal("failed", result.Outcome!.Status);
        Assert.Equal("not-dispatched", result.Outcome.Dispatch);
        Assert.Equal(0, result.Outcome.InputsSent);
        Assert.Equal("invalid-arguments", result.Outcome.Failure!.Code);
        Assert.Equal(message, result.Outcome.Failure.Message);
        Assert.False(result.Outcome.Failure.ActionDispatched);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(91)]
    public async Task LaunchWaitOutsideItsBoundIsRefusedBeforeStartingTheApp(int waitSeconds)
    {
        using var installed = new FakeDesktop().Install();
        using var sessions = new SessionManager(null, ProcessPolicy.AllowAll);

        AssertInvalid(
            await Run(new LaunchTool(sessions), new { app = "uninstalled-demo-app.exe", waitSeconds }),
            "waitSeconds must be between 1 and 90"
        );
    }

    [Fact]
    public void AColdLaunchCanRequestMoreWindowPollsWithoutChangingTheDefault()
    {
        Assert.Equal(SessionManager.LaunchPolls, SessionManager.LaunchPollCount(null));
        Assert.Equal(90, SessionManager.LaunchPollCount(45));
    }

    [Fact]
    public async Task AFillWithoutAValueIsAnInvalidArgumentsRefusal()
    {
        using var installed = new FakeDesktop().Install();

        AssertInvalid(
            await Run(new FillTool(new ElementRegistry()), new { @ref = "w1e1" }),
            "Missing required argument: value"
        );
    }

    [Fact]
    public async Task ATypeWithoutTextIsAnInvalidArgumentsRefusal()
    {
        using var installed = new FakeDesktop().Install();

        AssertInvalid(await Run(new TypeTool(new ElementRegistry()), new { }), "Missing required argument: text");
    }

    [Fact]
    public async Task VerifyFocusWithoutARefIsAnInvalidArgumentsRefusal()
    {
        using var installed = new FakeDesktop().Install();

        AssertInvalid(
            await Run(new SendKeysTool(new ElementRegistry()), new { chord = "Enter", verifyFocus = true }),
            "verifyFocus requires an element ref; no input sent."
        );
        AssertInvalid(
            await Run(
                new PasteTool(new ElementRegistry(), new PendingInvokeTracker(), ProcessPolicy.AllowAll, null),
                new { text = "x", verifyFocus = true }
            ),
            "verifyFocus requires an element ref; nothing was pasted."
        );
    }

    [Fact]
    public async Task AClickWithoutARefIsAnInvalidArgumentsRefusal()
    {
        using var installed = new FakeDesktop().Install();

        AssertInvalid(await Run(new ClickTool(new ElementRegistry()), new { }), "Missing required argument: ref");
    }

    [Fact]
    public async Task AMouseActionWithoutAHandleOrRefIsAnInvalidArgumentsRefusal()
    {
        using var installed = new FakeDesktop().Install();
        using var sessions = new SessionManager(null, ProcessPolicy.AllowAll);

        AssertInvalid(
            await Run(
                new MouseTool(new ElementRegistry(), new PendingInvokeTracker(), sessions),
                new { action = "park" }
            ),
            "handle or ref required"
        );
    }
}
