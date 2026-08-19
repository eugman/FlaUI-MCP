using PlaywrightWindows.Mcp;
using PlaywrightWindows.Mcp.Core;
using PlaywrightWindows.Mcp.Tools;

DpiUtility.EnablePerMonitorV2();

// Create shared services
var sessionManager = new SessionManager();
var elementRegistry = new ElementRegistry();
var invokeTracker = new PendingInvokeTracker();

// While tools are actively being called, hold a Windows power availability
// request (display required) so the screen does not turn off and the lock
// screen does not interrupt a long automation run. Released after a sliding
// idle period. Set FLAUI_MCP_KEEP_AWAKE_SECONDS to change the idle period,
// or to 0 to disable.
var keepAwakeSeconds = 300;
if (int.TryParse(Environment.GetEnvironmentVariable("FLAUI_MCP_KEEP_AWAKE_SECONDS"), out var configuredSeconds))
{
    keepAwakeSeconds = configuredSeconds;
}
using var keepAwake = keepAwakeSeconds > 0
    ? KeepAwake.CreateDisplayKeepAwake(
        TimeSpan.FromSeconds(keepAwakeSeconds),
        "FlaUI-MCP is driving Windows UI automation")
    : null;

// Register all tools
var toolRegistry = new ToolRegistry(onToolActivity: keepAwake != null ? keepAwake.Poke : null);
toolRegistry.RegisterTool(new LaunchTool(sessionManager));
toolRegistry.RegisterTool(new SnapshotTool(sessionManager, elementRegistry, invokeTracker));
toolRegistry.RegisterTool(new ClickTool(elementRegistry, invokeTracker));
toolRegistry.RegisterTool(new TypeTool(elementRegistry, invokeTracker));
toolRegistry.RegisterTool(new FillTool(elementRegistry, invokeTracker));
toolRegistry.RegisterTool(new GetTextTool(elementRegistry, invokeTracker));
toolRegistry.RegisterTool(new SendKeysTool(elementRegistry, invokeTracker));
toolRegistry.RegisterTool(new ScreenshotTool(sessionManager, elementRegistry, invokeTracker));
toolRegistry.RegisterTool(new ListWindowsTool(sessionManager));
toolRegistry.RegisterTool(new FocusWindowTool(sessionManager));
toolRegistry.RegisterTool(new CloseWindowTool(sessionManager));
toolRegistry.RegisterTool(new BatchTool(sessionManager, elementRegistry, invokeTracker));

// Create and run MCP server
var server = new McpServer(toolRegistry);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

try
{
    await server.RunAsync(cts.Token);
}
finally
{
    sessionManager.Dispose();
}
