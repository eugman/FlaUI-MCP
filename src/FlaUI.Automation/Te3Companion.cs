using System.Diagnostics;
using System.Text.Json;
using PlaywrightWindows.Mcp;
using PlaywrightWindows.Mcp.Core;

/// <summary>Attach-only TE3 navigation. The generic server remains application-independent and owns capture.</summary>
public sealed class Te3Companion(AutomationHost host, string name) : ToolBase
{
    // te3_inspect and te3_capture were removed after the five-condition study: agents never
    // called capture, and inspect only repeated windows_list_windows.
    public static readonly string[] Names = ["te3_navigate"];
    public override string Name => name;
    public override string Description => name == "te3_navigate"
        ? "Navigate an explicit TE3 process to a supported destination and verify arrival. Takes desktop focus; requires user handoff permission. No model edits, script execution, or dialog dismissal."
        : throw new ArgumentException("Unknown companion tool.");
    public override object InputSchema
    {
        get
        {
            var properties = new Dictionary<string, object>
            {
                ["processId"] = new { type = "integer", minimum = 1, description = "Explicit running TabularEditor3 PID; handles/refs from another MCP server are not accepted." },
                ["destination"] = new { type = "string", @enum = Te3Destinations.Ids }
            };
            foreach (var key in new[] { "table", "objectName" }) properties[key] = new { type = "string", description = "For destination object; table and objectName required." };
            properties["folder"] = new { type = "string", description = "Display folder path of the object, backslash-separated; required when the object sits in a display folder." };
            properties["objectType"] = new { type = "string", @enum = Te3Destinations.ObjectTypes };
            return new { type = "object", properties, required = new[] { "processId", "destination" }, additionalProperties = false };
        }
    }

    public override async Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var lastStep = "validate request";
        var phase = "validate";
        try
        {
            var a = arguments ?? JsonSerializer.SerializeToElement(new { });
            Validate(a);
            phase = "attach";
            using var process = Process.GetProcessById(a.GetProperty("processId").GetInt32());
            if (!string.Equals(process.ProcessName, "TabularEditor3", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("processId must identify TabularEditor3.");
            var windows = Win32Desktop.GetTopLevelWindows(process.Id);
            var hwnd = MainWindow(windows);
            var handle = host.Sessions.RegisterNativeWindow(hwnd, process.Id);
            var identity = host.Sessions.GetInputTarget(handle);
            if (host.Pending.TryGetPending(process.Id, out var pendingCall)) throw new InvalidOperationException(PendingInvokeTracker.DescribeBlocked(pendingCall));

            async Task Invoke(string tool, object args)
            {
                identity.EnsureAlive();
                OperationContext.Check();
                var result = await host.Tools.ExecuteToolAsync(tool, JsonSerializer.SerializeToElement(args));
                if (result.IsError == true || result.Outcome?.IsPending == true)
                    throw new InvalidOperationException(string.Join("; ", result.Content.Select(c => c.Text)));
            }
            var page = new Te3Page(host, handle, Invoke, step => lastStep = step);

            // Validate has already confirmed the id is in the registry.
            var destination = Te3Destinations.Find(GetStringArgument(a, "destination"))!;
            var hasPreferences = windows.Any(w => w.Title == "Preferences");
            if (windows.Any(w => w.Hwnd != hwnd && w.Title.Length > 0 && w.Title != "Preferences"))
                throw new InvalidOperationException("An additional TE3 window is open: " +
                    string.Join("; ", windows.Where(w => w.Hwnd != hwnd && w.Title.Length > 0 && w.Title != "Preferences")
                        .Select(w => $"{w.Title} (HWND {w.Hwnd}, tool window: {w.IsToolWindow})")) +
                    ". Inspect it; no dialog was dismissed.");
            if (hasPreferences && destination.Section == null) throw new InvalidOperationException("Preferences is open. Cancel it explicitly before navigating elsewhere.");

            phase = "navigate";
            lastStep = "navigate " + destination.Id;
            switch (destination)
            {
                case { Section: { } section }:
                    if (!hasPreferences) await page.OpenPreferences();
                    await page.SearchPreferences(section);
                    await page.SelectPreferencesSection(section);
                    break;
                case { Id: "object" }:
                    await page.SelectObjectPath(GetStringArgument(a, "table")!, GetStringArgument(a, "objectName")!, GetStringArgument(a, "objectType")!, GetStringArgument(a, "folder") ?? "");
                    break;
                case { Id: "tom-explorer" }:
                    await page.OpenTom();
                    break;
                case { Id: "expression-editor" }:
                    await page.SelectDocument("Expression Editor");
                    break;
                default:
                    throw new ArgumentException("No navigation for destination " + destination.Id);
            }
            lastStep = "verify " + destination.Id;
            phase = "verify-arrival";
            await page.VerifyCompanionDestination(destination, GetStringArgument(a, "table"), GetStringArgument(a, "objectName"), GetStringArgument(a, "objectType"));
            return Json(new { assessment = Assessment(true), destination = destination.Id, phase, lastStep });
        }
        catch (Exception ex)
        {
            return Failure(phase, lastStep, ex.Message);
        }
    }

    internal static McpToolResult Failure(string phase, string lastStep, string error)
    {
        var blocker = error.Contains("activation-denied:", StringComparison.Ordinal) ? "activation-denied"
            : error.Contains("desktop-unavailable:", StringComparison.Ordinal) ? "desktop-unavailable" : null;
        var next = blocker switch
        {
            "activation-denied" => "Windows refused to activate TE3; retrying will not help. The blocked step sent no input, but earlier steps may have. " +
                "Ask the user to click the TE3 title bar, then check windows_list_windows before navigating again. In unattended runs, stop.",
            "desktop-unavailable" => "No usable desktop session was observed. The blocked step sent no input, but earlier steps may have. " +
                "Stop if the session is locked, disconnected or unattended.",
            _ => "Inspect state before continuing; no uncertain input is automatically replayed."
        };
        return ErrorResult(JsonSerializer.Serialize(new { assessment = Assessment(false), phase, lastStep, error, blocker, next }, McpProtocol.JsonOptions));
    }

    internal static void Validate(JsonElement a)
    {
        if (a.ValueKind != JsonValueKind.Object) throw new ArgumentException("Arguments must be an object.");
        if (!a.TryGetProperty("processId", out var pid) || !pid.TryGetInt32(out var id) || id < 1) throw new ArgumentException("Positive processId required.");
        if (!a.TryGetProperty("destination", out var d) || Te3Destinations.Find(d.GetString()) is not { } destination)
            throw new ArgumentException($"Unsupported destination; use one of: {string.Join(", ", Te3Destinations.Ids)}.");
        if (destination.Id == "object")
        {
            foreach (var field in new[] { "table", "objectName", "objectType" })
                if (!a.TryGetProperty(field, out var value) || string.IsNullOrWhiteSpace(value.GetString())) throw new ArgumentException("Object destination requires " + field);
            if (!Te3Destinations.ObjectTypes.Contains(a.GetProperty("objectType").GetString())) throw new ArgumentException("Unsupported objectType.");
        }
    }

    private static McpToolResult Json(object value) => TextResult(JsonSerializer.Serialize(value, McpProtocol.JsonOptions));

    // A matched scene is not a visual review; the agent still has to look at its capture.
    internal static object Assessment(bool sceneMatched) => new {
        sceneIdentity = sceneMatched ? "matched" : "not-assessed",
        visualCompleteness = "not-assessed", docsApproved = false
    };

    internal static nint MainWindow(IReadOnlyList<Win32WindowInfo> windows)
    {
        // Process.MainWindowHandle can select an untitled popup after tree navigation.
        var main = windows.Where(w => !w.IsToolWindow && !w.IsCloaked &&
            w.Title.Contains("Tabular Editor 3", StringComparison.Ordinal)).ToArray();
        if (main.Length != 1) throw new InvalidOperationException("Expected one TE3 document window; no launch or activation attempted.");
        return main[0].Hwnd;
    }
}
