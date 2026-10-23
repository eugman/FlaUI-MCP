using System.Text.Json;
using FlaUI.Mcp;
using FlaUI.Mcp.Core;
using Xunit;

public sealed class McpCompositionTests
{
    [Fact]
    public void EndpointIncludesWindowsToolsAndWarnsForTerminal()
    {
        var policy = new ProcessPolicy(["TabularEditor3"], advisory: true);
        using var host = new AutomationHost(policy, includeDesktopTools: true);
        Te3CrossAppPolicy.Install(host.Tools, policy);
        var names = host.Tools.GetToolDefinitions().Select(tool => tool.Name).ToArray();

        Assert.True(policy.IsAdvisory);
        Assert.Contains("windows_list_windows", names);
        Assert.Contains("windows_launch", names);
        Assert.Contains("windows_get_text", names);
        Assert.Contains(
            "outside the preferred apps",
            host.Tools.AdvisoryWarning!(
                "windows_launch",
                JsonSerializer.SerializeToElement(new { app = "wt.exe" }),
                null
            )
        );
        Assert.Null(
            host.Tools.AdvisoryWarning!(
                "windows_launch",
                JsonSerializer.SerializeToElement(new { app = "TabularEditor3.exe" }),
                null
            )
        );
    }
}
