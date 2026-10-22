using System.Text.Json;
using FlaUI.Mcp.Core;

namespace FlaUI.Mcp.Tools;

/// <summary>
/// Launch a Windows application
/// </summary>
public class LaunchTool : ToolBase
{
    private readonly SessionManager _sessionManager;

    public LaunchTool(SessionManager sessionManager)
    {
        _sessionManager = sessionManager;
    }

    public override string Name => "windows_launch";

    public override McpToolAnnotations Annotations => McpToolAnnotations.Input(destructive: false);

    public override string Description =>
        "Launch a Windows application. Returns a window handle for use with other tools.";

    public override object InputSchema => ToolSchema.For<Arguments>();

    /// <summary>The tool's arguments; its schema is generated from them.</summary>
    internal sealed record Arguments
    {
        [ToolDoc(
            "Path to executable or UWP app ID (e.g., 'calc.exe', 'notepad.exe', 'C:\\\\Program Files\\\\MyApp\\\\app.exe')"
        )]
        public required string App { get; init; }

        [ToolDoc("Optional command line arguments")]
        public string[]? Args { get; init; }
    }

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var app = GetStringArgument(arguments, "app");
        if (string.IsNullOrEmpty(app))
        {
            return Task.FromResult(ErrorResult("Missing required argument: app"));
        }

        var args = GetArgument<string[]>(arguments, "args");

        try
        {
            var (handle, window) = _sessionManager.LaunchApp(app, args);
            return Task.FromResult(TextResult($"Launched {app}\nWindow handle: {handle}\nTitle: {window.Title}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ErrorResult($"Failed to launch {app}: {ex.Message}"));
        }
    }
}
