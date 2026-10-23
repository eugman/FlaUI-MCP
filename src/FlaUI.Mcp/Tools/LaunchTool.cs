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

        [ToolDoc(
            "Poll for a unique window from this process for up to this many seconds after startup (1-90; default 20). Cold starts such as TE3 can need 45 or more; allow a longer caller timeout."
        )]
        public int? WaitSeconds { get; init; }
    }

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var app = GetStringArgument(arguments, "app");
        if (string.IsNullOrEmpty(app))
        {
            return Task.FromResult(ErrorResult(RefusalCodes.InvalidArguments, "Missing required argument: app"));
        }

        var args = GetArgument<string[]>(arguments, "args");
        var waitSeconds = GetArgument<int?>(arguments, "waitSeconds");
        if (waitSeconds is < 1 or > 90)
        {
            return Task.FromResult(ErrorResult(RefusalCodes.InvalidArguments, "waitSeconds must be between 1 and 90"));
        }

        try
        {
            var (handle, window) = _sessionManager.LaunchApp(app, args, waitSeconds);
            return Task.FromResult(TextResult($"Launched {app}\nWindow handle: {handle}\nTitle: {window.Title}"));
        }
        catch (LaunchPendingException pending)
        {
            return Task.FromResult(PendingResult(pending));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ErrorResult($"Failed to launch {app}: {ex.Message}"));
        }
    }

    internal static McpToolResult PendingResult(LaunchPendingException pending) =>
        TextResult(pending.Message) with
        {
            Outcome = new ToolOutcome("partial", "not-tracked")
            {
                Status = "unverified",
                Verification = "not-established",
                Failure = new InputRefusal("launch-pending", pending.Message) { ActionDispatched = true },
                Details = new Dictionary<string, JsonElement>
                {
                    ["processId"] = JsonSerializer.SerializeToElement(pending.ProcessId),
                    ["observedWindows"] = JsonSerializer.SerializeToElement(
                        pending
                            .ObservedWindows.Select(window => new { hwnd = (long)window.Hwnd, title = window.Title })
                            .ToArray()
                    ),
                },
            },
        };
}
