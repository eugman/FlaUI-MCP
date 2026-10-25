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
        "Start a Windows application and return a provisional window handle. Use windows_list_windows to identify the final app-specific window by title, process, and expected controls before input; a startup splash can be stable before the main window appears.";

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
            return Task.FromResult(ProvisionalResult(app, handle, window.Properties.ProcessId.Value, window.Title));
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

    /// <summary>A stable owned window is still provisional: a splash can later be replaced.</summary>
    internal static McpToolResult ProvisionalResult(string app, string handle, int processId, string title) =>
        TextResult(
            $"Started {app} (PID {processId}). Provisional window handle: {handle}\nTitle: {title}\n"
                + "Use windows_list_windows, then check the app-specific title and expected controls before input; this may be a splash."
        ) with
        {
            Outcome = new ToolOutcome("completed", "not-tracked")
            {
                Status = "unverified",
                Verification = "provisional-window",
                Failure = new InputRefusal("launch-provisional", "Window identity is not yet confirmed")
                {
                    ActionDispatched = true,
                },
                Details = new Dictionary<string, JsonElement>
                {
                    ["processId"] = JsonSerializer.SerializeToElement(processId),
                    ["windowHandle"] = JsonSerializer.SerializeToElement(handle),
                },
            },
        };

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
