using System.Text.Json;
using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

[CollectionDefinition("Native host construction", DisableParallelization = true)]
public sealed class NativeHostConstructionCollection { }

// UIA service construction touches native process state, even without desktop calls.
// Keep these tests isolated; pure contract/assertion tests still run in parallel.
[Collection("Native host construction")]
public sealed class ToolCompositionTests
{
    [Fact]
    public void McpAndRunnerAdvertiseIdenticalSharedContracts()
    {
        // Construct services only: no desktop discovery, launch, focus, or input calls.
        using var runner = new AutomationHost(new ProcessPolicy(["test-app"]));
        using var mcp = new AutomationHost(new ProcessPolicy(["test-app"]), includeDesktopTools: true);
        var shared = runner.Tools.GetToolDefinitions().ToDictionary(tool => tool.Name);
        var all = mcp.Tools.GetToolDefinitions().ToDictionary(tool => tool.Name);
        // 14: the original eleven plus windows_get_clipboard, which the runner needs
        // because a Scintilla editor exposes no text to read back any other way, and
        // windows_set_window_state and windows_mouse, which shot setup needs.
        Assert.Equal(14, shared.Count);
        Assert.Contains("windows_paste", shared.Keys);
        foreach (var (name, definition) in shared)
        {
            Assert.Equal(JsonSerializer.Serialize(definition), JsonSerializer.Serialize(all[name]));
        }

        Assert.Equal(
            new[]
            {
                "windows_batch",
                "windows_close",
                "windows_focus",
                "windows_get_text",
                "windows_launch",
                "windows_list_windows",
            },
            all.Keys.Except(shared.Keys).Order(StringComparer.Ordinal)
        );
        Assert.NotNull(runner.Tools.ResolveTarget);
        Assert.NotNull(mcp.Tools.ResolveTarget);
    }

    [Theory]
    [InlineData("windows_operation_status", true, false)]
    [InlineData("windows_find", true, false)]
    [InlineData("windows_wait", true, false)]
    [InlineData("windows_snapshot", true, false)]
    [InlineData("windows_get_text", true, false)]
    [InlineData("windows_list_windows", true, false)]
    [InlineData("windows_get_clipboard", true, false)]
    [InlineData("windows_screenshot", false, false)]
    [InlineData("windows_close", false, true)]
    [InlineData("windows_fill", false, true)]
    [InlineData("windows_send_keys", false, true)]
    [InlineData("windows_click", false, true)]
    [InlineData("windows_type", false, false)]
    [InlineData("windows_focus", false, false)]
    [InlineData("windows_launch", false, false)]
    [InlineData("windows_set_window_state", false, false)]
    public async Task ToolsListAnnotatesWhetherEachToolSendsInput(string name, bool readOnly, bool destructive)
    {
        using var host = new AutomationHost(new ProcessPolicy(["test-app"]), includeDesktopTools: true);
        var response = await new McpServer(host.Tools).HandleRequestAsync(
            JsonSerializer.Deserialize<JsonRpcRequest>("{\"id\":1,\"method\":\"tools/list\"}")!
        );

        var tools = JsonSerializer.SerializeToElement(response!.Result, McpProtocol.JsonOptions).GetProperty("tools");
        var tool = tools.EnumerateArray().Single(t => t.GetProperty("name").GetString() == name);
        var annotations = tool.GetProperty("annotations");
        Assert.Equal(readOnly, annotations.GetProperty("readOnlyHint").GetBoolean());
        Assert.Equal(destructive, annotations.GetProperty("destructiveHint").GetBoolean());
        Assert.False(annotations.GetProperty("openWorldHint").GetBoolean());
        var idempotent = annotations.GetProperty("idempotentHint").GetBoolean();
        Assert.True(!readOnly || idempotent, "a read-only tool is idempotent");
        Assert.All(tools.EnumerateArray(), t => Assert.True(t.TryGetProperty("annotations", out _)));
    }

    [Fact]
    public async Task OperationStatusEchoesTheToolsAnnotation()
    {
        using var host = new AutomationHost(new ProcessPolicy(["test-app"]));
        await host.Tools.ExecuteToolAsync("windows_operation_status", null);

        var status = JsonSerializer.SerializeToElement(host.Tools.Operations.Status())[0];

        Assert.True(status.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean());
    }

    [Fact]
    public async Task ActivityHookIsInvokedByCallsNotRegistration()
    {
        var activity = 0;
        using var host = new AutomationHost(new ProcessPolicy(["ContosoStudio"]), onToolActivity: () => activity++);
        Assert.Equal(0, activity);
        // Operation status does not call UI Automation or resolve a desktop target.
        var result = await host.Tools.ExecuteToolAsync("windows_operation_status", null);
        Assert.NotEqual(true, result.IsError);
        Assert.Equal(1, activity);
    }
}
