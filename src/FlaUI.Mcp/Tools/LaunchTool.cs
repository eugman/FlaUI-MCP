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
        "Start a Windows application and return a provisional handle when one eligible window is found, or one titled unowned window has only its owned dialogs alongside it. windowTitle can select an exact title when needed. A pending or handed-off launch has no proven window handle: inspect windows_list_windows and attach explicitly before input. A startup splash can be stable before the main window appears.";

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
            "Poll for an eligible provisional window from this process for up to 1-90 seconds (default 20). The launch call allows 15 more seconds for startup and provider reads; set the client timeout above waitSeconds + 15. An ambiguous or slower launch returns an unresolved outcome for inspection."
        )]
        public int? WaitSeconds { get; init; }

        [ToolDoc(
            "Optional exact target-window title to select when a startup modal or splash also belongs to the process. The returned handle remains provisional; inspect before input."
        )]
        public string? WindowTitle { get; init; }
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
        var windowTitle = GetStringArgument(arguments, "windowTitle");
        if (waitSeconds is < 1 or > 90)
        {
            return Task.FromResult(ErrorResult(RefusalCodes.InvalidArguments, "waitSeconds must be between 1 and 90"));
        }

        if (windowTitle != null && string.IsNullOrWhiteSpace(windowTitle))
        {
            return Task.FromResult(ErrorResult(RefusalCodes.InvalidArguments, "windowTitle must not be blank"));
        }

        try
        {
            var (handle, window) = _sessionManager.LaunchApp(app, args, waitSeconds, windowTitle);
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

    public override TimeSpan? TimeoutForArguments(JsonElement? arguments)
    {
        if (
            arguments is not { ValueKind: JsonValueKind.Object } args
            || !args.TryGetProperty("waitSeconds", out var raw)
        )
        {
            return TimeSpan.FromSeconds(SessionManager.LaunchPolls / 2 + 15);
        }

        return
            raw.ValueKind == JsonValueKind.Number
            && raw.TryGetInt32(out var waitSeconds)
            && waitSeconds is >= 1 and <= 90
            ? TimeSpan.FromSeconds(waitSeconds + 15)
            : null;
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

    internal static McpToolResult PendingResult(LaunchPendingException pending)
    {
        var details = new Dictionary<string, JsonElement>
        {
            ["observedWindows"] = JsonSerializer.SerializeToElement(
                pending
                    .ObservedWindows.Select(window => new { hwnd = (long)window.Hwnd, title = window.Title })
                    .ToArray()
            ),
        };
        if (pending.ProcessId > 0)
        {
            details["processId"] = JsonSerializer.SerializeToElement(pending.ProcessId);
        }

        return TextResult(pending.Message) with
        {
            Outcome = new ToolOutcome("partial", "not-tracked")
            {
                Status = "unverified",
                Verification = "not-established",
                Failure = new InputRefusal(pending.Handoff ? "launch-handoff" : "launch-pending", pending.Message)
                {
                    ActionDispatched = true,
                },
                Details = details,
            },
        };
    }
}
