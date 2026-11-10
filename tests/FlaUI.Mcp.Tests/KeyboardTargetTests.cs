using System.Text.Json;
using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// Type, send keys and paste resolve their target the same way: a ref, a window handle, or the foreground. A handle
/// whose process is off the app allowlist is refused before any focus or input with the same typed code the
/// foreground path gives, and argument errors found before input are typed invalid-arguments.
/// </summary>
[Collection(InputLeaseCollection.Name)]
public sealed class KeyboardTargetTests
{
    private static JsonElement Args(object value) => JsonSerializer.SerializeToElement(value);

    private static async Task<McpToolResult> Run(ToolBase tool, object arguments)
    {
        var registry = new ToolRegistry();
        registry.RegisterTool(tool);
        return await registry.ExecuteToolAsync(tool.Name, Args(arguments));
    }

    private static (SessionManager Sessions, string Handle) Window()
    {
        var sessions = new SessionManager(null, ProcessPolicy.AllowAll);
        var handle = sessions.PublishWindow(null, FakeDesktop.TargetHwnd, FakeDesktop.TargetPid, FakeDesktop.Target);
        return (sessions, handle);
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

    [Theory]
    [InlineData("type")]
    [InlineData("keys")]
    [InlineData("paste")]
    public async Task AHandleOffTheAllowlistIsATypedRefusalBeforeAnyInput(string tool)
    {
        var desktop = new FakeDesktop { ForegroundWindow = FakeDesktop.OtherHwnd };
        using var installed = desktop.Install();
        var (sessions, handle) = Window();
        using var _ = sessions;
        var policy = new ProcessPolicy(["some-allowed-app"]);
        var refs = new ElementRegistry();
        var pending = new PendingInvokeTracker();

        var result = tool switch
        {
            "type" => await Run(new TypeTool(refs, pending, policy, sessions), new { handle, text = "a" }),
            "keys" => await Run(new SendKeysTool(refs, pending, policy, sessions), new { handle, chord = "Enter" }),
            _ => await Run(new PasteTool(refs, pending, policy, sessions), new { handle, text = "a" }),
        };

        AssertTyped(result, "process-not-allowed", desktop);
        Assert.StartsWith("Target process is not in the FlaUI-MCP app allowlist.", result.Content[0].Text);
        Assert.Null(desktop.ClipboardWritten);
    }

    [Theory]
    [InlineData("type")]
    [InlineData("keys")]
    [InlineData("paste")]
    public async Task AHandleOnTheAllowlistTakesTheKeys(string tool)
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        var (sessions, handle) = Window();
        using var _ = sessions;
        var policy = new ProcessPolicy([desktop.ProcessNames[FakeDesktop.TargetPid]]);
        var refs = new ElementRegistry();
        var pending = new PendingInvokeTracker();

        var result = tool switch
        {
            "type" => await Run(new TypeTool(refs, pending, policy, sessions), new { handle, text = "a" }),
            "keys" => await Run(new SendKeysTool(refs, pending, policy, sessions), new { handle, chord = "Enter" }),
            _ => await Run(new PasteTool(refs, pending, policy, sessions), new { handle, text = "a" }),
        };

        Assert.True(result.IsError != true, result.Content[0].Text);
        Assert.True(desktop.Dispatched > 0);
    }
}
