using System.Text.Json;
using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// Argument errors a tool finds before any input carry the typed code invalid-arguments, so a client reads the
/// code instead of the text and a broker knows nothing was sent.
/// </summary>
public sealed class PreInputArgumentTests
{
    private static JsonElement Args(object value) => JsonSerializer.SerializeToElement(value);

    private static async Task<McpToolResult> Run(ToolBase tool, object arguments)
    {
        var registry = new ToolRegistry();
        registry.RegisterTool(tool);
        return await registry.ExecuteToolAsync(tool.Name, Args(arguments));
    }

    private static void AssertTyped(McpToolResult result, string code, FakeDesktop desktop)
    {
        Assert.True(result.IsError);
        Assert.Equal("failed", result.Outcome!.Status);
        Assert.Equal("not-dispatched", result.Outcome.Dispatch);
        Assert.Equal(code, result.Outcome.Failure!.Code);
        Assert.False(result.Outcome.Failure.ActionDispatched);
        Assert.Equal(0, desktop.Dispatched);
        Assert.Equal(0, desktop.FocusAttempts);
    }

    [Fact]
    public async Task SendKeysWithoutAChordOrKeysIsAnInvalidArgumentsRefusal()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();

        AssertTyped(await Run(new SendKeysTool(new ElementRegistry()), new { }), "invalid-arguments", desktop);
    }

    [Fact]
    public async Task SendKeysWithBothAChordAndKeysIsAnInvalidArgumentsRefusal()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();

        AssertTyped(
            await Run(new SendKeysTool(new ElementRegistry()), new { chord = "Enter", keys = new[] { "Tab" } }),
            "invalid-arguments",
            desktop
        );
    }

    [Fact]
    public async Task FocusWithoutAHandleOrTitleIsAnInvalidArgumentsRefusal()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        using var sessions = new SessionManager(null, ProcessPolicy.AllowAll);

        AssertTyped(await Run(new FocusWindowTool(sessions), new { }), "invalid-arguments", desktop);
    }

    [Fact]
    public async Task CloseWithoutAHandleIsAnInvalidArgumentsRefusal()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        using var sessions = new SessionManager(null, ProcessPolicy.AllowAll);

        AssertTyped(await Run(new CloseWindowTool(sessions), new { }), "invalid-arguments", desktop);
    }

    [Fact]
    public async Task ABatchOverItsStepLimitIsAnInvalidArgumentsRefusal()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        var registry = new ToolRegistry();
        var batch = new BatchTool(registry);
        registry.RegisterTool(batch);

        var result = await registry.ExecuteToolAsync(
            batch.Name,
            Args(new { actions = Enumerable.Repeat(new { action = "wait", ms = 0 }, 101) })
        );

        AssertTyped(result, "invalid-arguments", desktop);
    }
}
