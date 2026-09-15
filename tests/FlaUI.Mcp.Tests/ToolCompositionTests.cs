using System.Text.Json;
using PlaywrightWindows.Mcp.Core;
using PlaywrightWindows.Mcp.Tools;
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
            Assert.Equal(11, shared.Count);
            Assert.Contains("windows_paste", shared.Keys);
            foreach (var (name, definition) in shared)
                Assert.Equal(JsonSerializer.Serialize(definition), JsonSerializer.Serialize(all[name]));
            Assert.Equal(new[] { "windows_batch", "windows_close", "windows_focus", "windows_get_text", "windows_launch", "windows_list_windows" },
                all.Keys.Except(shared.Keys).Order(StringComparer.Ordinal));
            Assert.NotNull(runner.Tools.ResolveTarget);
            Assert.NotNull(mcp.Tools.ResolveTarget);
    }

    [Fact]
    public async Task ActivityHookIsInvokedByCallsNotRegistration()
    {
        var activity = 0;
        using var host = new AutomationHost(new ProcessPolicy(["TabularEditor3"]), onToolActivity: () => activity++);
        Assert.Equal(0, activity);
        // Operation status does not call UI Automation or resolve a desktop target.
        var result = await host.Tools.ExecuteToolAsync("windows_operation_status", null);
        Assert.NotEqual(true, result.IsError);
        Assert.Equal(1, activity);
    }
}
