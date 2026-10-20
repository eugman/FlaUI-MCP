using FlaUI.Mcp.Core;

/// <summary>The mcp command: the generic windows_* tools and the TE3 tools in one server.</summary>
internal static class Te3McpServer
{
    public static async Task Run()
    {
        DpiUtility.EnablePerMonitorV2();
        // One server for everything: the generic windows_* tools (with the window-management
        // extensions) and the TE3 tools share one registry, so refs and handles carry across.
        using var host = new AutomationHost(new ProcessPolicy(["TabularEditor3"]), includeDesktopTools: true);
        using var interaction = new Te3Interaction();
        var registry = host.Tools;
        foreach (var name in Te3InteractionTool.Names)
        {
            registry.RegisterTool(new Te3InteractionTool(interaction, name));
        }

        foreach (var name in Te3Companion.Names)
        {
            registry.RegisterTool(new Te3Companion(host, name, interaction));
        }

        registry.RegisterTool(new Te3Layout(host, interaction));
        registry.RegisterTool(FlaUI.Mcp.Te3.Te3NativePreflightTool.Create());

        await new FlaUI.Mcp.McpServer(registry).RunAsync();
    }
}
