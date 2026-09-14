using System.Diagnostics;
using System.Text.Json;
using PlaywrightWindows.Mcp;
using PlaywrightWindows.Mcp.Core;

/// <summary>Attach-only TE3 tools. The generic server remains application-independent.</summary>
public sealed class Te3Companion(AutomationHost host, string name) : ToolBase
{
    public static readonly string[] Names = ["te3_catalog", "te3_inspect", "te3_navigate", "te3_capture"];
    public override string Name => name;
    public override string Description => name switch {
        "te3_catalog" => "Read a compact TE3 map index or one topic. No desktop access. Read only the destination topic plus capture for screenshot tasks.",
        "te3_inspect" => "Read bounded TE3 state for an explicit processId: windows, preferences, or object. Does not focus. Unknown state is not readiness.",
        "te3_navigate" => "Navigate an explicit TE3 process to a supported destination and verify arrival. Takes desktop focus; requires user handoff permission. No model edits, script execution, or dialog dismissal.",
        "te3_capture" => "Save a native whole-window PNG of an already prepared TE3 scene. No navigation or resizing. Scene identity is checked; visual completeness is NOT assessed. Display the image before claiming screenshot success. Requires desktop permission.",
        _ => throw new ArgumentException("Unknown companion tool.")
    };
    public override object InputSchema
    {
        get
        {
            var properties = new Dictionary<string, object>();
            if (name == "te3_catalog") properties["topic"] = new { type = "string", @enum = Te3Guide.Topics.Keys.ToArray(), description = "Omit for index. Map topic IDs are not navigation destination IDs." };
            else
            {
                properties["processId"] = new { type = "integer", minimum = 1, description = "Explicit running TabularEditor3 PID; handles/refs from another MCP server are not accepted." };
                if (name == "te3_inspect") properties["topic"] = new { type = "string", @enum = Te3Destinations.InspectTopics, @default = "windows" };
                else properties[name == "te3_capture" ? "scene" : "destination"] = new { type = "string", @enum = Te3Destinations.Ids };
                foreach (var key in new[] { "table", "objectName", "folder" }) properties[key] = new { type = "string", description = "For destination/scene object; table and objectName required." };
                properties["objectType"] = new { type = "string", @enum = Te3Destinations.ObjectTypes };
                if (name == "te3_capture")
                {
                    properties["savePath"] = new { type = "string", description = "Absolute local PNG; existing files are never overwritten." };
                    properties["includeImage"] = new { type = "boolean", @default = false };
                }
            }
            string[] required = name switch {
                "te3_catalog" => [], "te3_inspect" => ["processId"],
                "te3_navigate" => ["processId", "destination"], _ => ["processId", "scene", "savePath"]
            };
            return new { type = "object", properties, required, additionalProperties = false };
        }
    }

    public override async Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var lastStep = "validate request";
        var phase = "validate";
        string? savedPath = null;
        try
        {
            var a = arguments ?? JsonSerializer.SerializeToElement(new { });
            Validate(name, a);
            if (name == "te3_catalog") return Json(Te3Guide.Read(GetStringArgument(a, "topic")));
            phase = "attach";
            using var process = Process.GetProcessById(a.GetProperty("processId").GetInt32());
            if (!string.Equals(process.ProcessName, "TabularEditor3", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("processId must identify TabularEditor3.");
            var windows = Win32Desktop.GetTopLevelWindows(process.Id);
            var hwnd = MainWindow(windows);
            var handle = host.Sessions.RegisterNativeWindow(hwnd, process.Id);
            var identity = host.Sessions.GetInputTarget(handle);
            var pending = host.Pending.TryGetPending(process.Id, out var pendingCall);
            if (name == "te3_inspect" && (GetStringArgument(a, "topic") ?? "windows") == "windows")
                return Json(new { processId = process.Id, version = process.MainModule?.FileVersionInfo.FileVersion,
                    mapRevision = Te3Guide.Revision, foreground = Win32Desktop.GetForegroundWindowProcessId() == process.Id,
                    pending, pendingScope = "operations tracked by this companion process only", blocker = pending ? PendingInvokeTracker.DescribeBlocked(pendingCall!) : null,
                    windows = windows.Select(w => new { w.Title, hwnd = (long)w.Hwnd }),
                    hwndMeaning = "Native HWND, diagnostic only; not a generic MCP handle. Use windows_list_windows for that server's handles.", readiness = "not-assessed" });
            if (pending) throw new InvalidOperationException(PendingInvokeTracker.DescribeBlocked(pendingCall!));

            async Task Invoke(string tool, object args)
            {
                identity.EnsureAlive();
                OperationContext.Check();
                var result = await host.Tools.ExecuteToolAsync(tool, JsonSerializer.SerializeToElement(args));
                if (result.IsError == true || result.Outcome?.IsPending == true)
                    throw new InvalidOperationException(string.Join("; ", result.Content.Select(c => c.Text)));
            }
            var page = new Te3Page(host, handle, Invoke, step => lastStep = step);
            var query = new ElementQuery(host.Sessions, host.Elements, host.Pending);
            if (name == "te3_inspect")
            {
                phase = "inspect";
                if (GetStringArgument(a, "topic") == "object")
                    return Json(new { mapRevision = Te3Guide.Revision,
                        properties = new[] { "Name", "Object Type", "DAX identifier" }.Select(label =>
                            query.Find(handle, new(Name: label, ControlType: "DataItem"), new(AutomationId: "PropertyGridView"), maxResults: 3, maxNodes: 500)).ToArray(),
                        readiness = "not-assessed; compare identity with the requested object" });
                var preferencesHandle = page.PreferencesHandle();
                var rows = query.Find(preferencesHandle, new(ControlType: "TreeItem"), new(AutomationId: "treePreferences"),
                    maxResults: 100, maxNodes: 1000);
                var selectedRows = rows.Elements.Where(r => r.Selected == true).ToArray();
                var selected = selectedRows.Length == 1 ? selectedRows[0].Value : null;
                var pane = Te3Destinations.All.Any(d => d.Section != null && d.Section == selected) ? "DAX Editor." + selected : null;
                var controls = pane == null ? null : query.Find(preferencesHandle, new(Visible: true), new(AutomationId: pane), maxResults: 30, maxNodes: 200);
                return Json(new { mapRevision = Te3Guide.Revision, selectedRows, rows.Complete, rows.Scope, controls,
                    readiness = "not-assessed; selection/property observations alone do not prove active content" });
            }

            // Validate has already confirmed the id is in the registry.
            var destination = Te3Destinations.Find(GetStringArgument(a, name == "te3_capture" ? "scene" : "destination"))!;
            var hasPreferences = windows.Any(w => w.Title == "Preferences");
            if (windows.Any(w => w.Hwnd != hwnd && w.Title.Length > 0 && w.Title != "Preferences"))
                throw new InvalidOperationException("An additional TE3 window is open: " +
                    string.Join("; ", windows.Where(w => w.Hwnd != hwnd && w.Title.Length > 0 && w.Title != "Preferences")
                        .Select(w => $"{w.Title} (HWND {w.Hwnd}, tool window: {w.IsToolWindow})")) +
                    ". Inspect it; no dialog was dismissed.");
            if (hasPreferences && destination.Section == null) throw new InvalidOperationException("Preferences is open. Cancel it explicitly before navigating elsewhere.");
            if (name == "te3_navigate")
            {
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
            }
            lastStep = "verify " + destination.Id;
            phase = name == "te3_capture" ? "verify-prepared-scene" : "verify-arrival";
            await page.VerifyCompanionDestination(destination, GetStringArgument(a, "table"), GetStringArgument(a, "objectName"), GetStringArgument(a, "objectType"));
            if (name == "te3_navigate") return Json(new { assessment = Assessment(true, null), destination = destination.Id, phase, lastStep, mapRevision = Te3Guide.Revision });

            lastStep = "capture " + destination.Id;
            phase = "capture";
            var target = host.Sessions.GetWindow(destination.Section != null ? page.PreferencesHandle() : handle)!;
            var targetHwnd = target.Properties.NativeWindowHandle.ValueOrDefault;
            var before = CaptureEnvironment.Observe(targetHwnd);
            var frame = FrameForWindow(before.MainWindowBounds, before.WorkingArea,
                target.Patterns.Window.IsSupported && target.Patterns.Window.Pattern.WindowVisualState.Value == FlaUI.Core.Definitions.WindowVisualState.Maximized);
            identity.EnsureAlive();
            var targetRef = host.Elements.Register(handle, target);
            var capture = await host.Tools.ExecuteToolAsync("windows_screenshot", JsonSerializer.SerializeToElement(new {
                @ref = targetRef, strictNative = true, savePath = GetStringArgument(a, "savePath"),
                includeImage = GetBoolArgument(a, "includeImage"), includeMetadata = true, frame
            }));
            if (capture.IsError == true)
                return Failure(phase, lastStep, string.Join("; ", capture.Content.Select(c => c.Text)), arguments);
            savedPath = GetStringArgument(a, "savePath");
            if (CaptureEnvironment.Observe(targetHwnd) != before)
                throw new InvalidOperationException("Display context changed during capture. Saved PNG is unverified; inspect it before retrying.");
            phase = "verify-captured-scene";
            await page.VerifyCompanionDestination(destination, GetStringArgument(a, "table"), GetStringArgument(a, "objectName"), GetStringArgument(a, "objectType"));
            capture.Content.Add(new McpContent { Text = JsonSerializer.Serialize(new { assessment = Assessment(true, savedPath), scene = destination.Id, environment = before, mapRevision = Te3Guide.Revision }, McpProtocol.JsonOptions) });
            return capture;
        }
        catch (Exception ex)
        {
            return Failure(phase, lastStep, ex.Message, arguments, savedPath);
        }
    }

    internal static McpToolResult Failure(string phase, string lastStep, string error, JsonElement? arguments, string? savedPath = null)
    {
        Dictionary<string, JsonElement>? navigation = null;
        if (phase == "verify-prepared-scene" && arguments is { } request)
        {
            navigation = request.EnumerateObject()
                .Where(p => p.Name is "processId" or "table" or "objectName" or "objectType" or "folder")
                .ToDictionary(p => p.Name, p => p.Value.Clone());
            navigation["destination"] = request.GetProperty("scene").Clone();
        }
        var blocker = error.Contains("activation-denied:", StringComparison.Ordinal) ? "activation-denied"
            : error.Contains("desktop-unavailable:", StringComparison.Ordinal) ? "desktop-unavailable" : null;
        var next = blocker switch
        {
            "activation-denied" => "Windows refused to activate TE3; retrying will not help. The blocked step sent no input, but earlier steps may have. " +
                "Ask the user to click the TE3 title bar, then te3_inspect before navigating again. In unattended runs, stop.",
            "desktop-unavailable" => "No usable desktop session was observed. The blocked step sent no input, but earlier steps may have. " +
                "te3_inspect to confirm; stop if the session is locked, disconnected or unattended.",
            _ => navigation == null ? "Inspect state before continuing; no uncertain input is automatically replayed."
                : "Capture did not navigate or save a PNG. Inspect current state. Only if the requested scene is not prepared, use te3_navigate with navigationArguments, then capture again. A provider failure does not prove missing selection; do not replay uncertain input."
        };
        return ErrorResult(JsonSerializer.Serialize(new { assessment = Assessment(false, savedPath), phase, lastStep, error,
            blocker, next, navigationArguments = navigation }, McpProtocol.JsonOptions));
    }

    internal static void Validate(string tool, JsonElement a)
    {
        if (a.ValueKind != JsonValueKind.Object) throw new ArgumentException("Arguments must be an object.");
        if (tool == "te3_catalog") return;
        if (!a.TryGetProperty("processId", out var pid) || !pid.TryGetInt32(out var id) || id < 1) throw new ArgumentException("Positive processId required.");
        if (tool == "te3_inspect")
        {
            if (a.TryGetProperty("topic", out var topic) && !Te3Destinations.InspectTopics.Contains(topic.GetString())) throw new ArgumentException("Unknown inspection topic.");
            return;
        }
        var key = tool == "te3_capture" ? "scene" : "destination";
        if (!a.TryGetProperty(key, out var d) || Te3Destinations.Find(d.GetString()) is not { } destination)
            throw new ArgumentException("Unsupported " + key + "; read te3_catalog.");
        if (destination.Id == "object")
        {
            foreach (var field in new[] { "table", "objectName", "objectType" })
                if (!a.TryGetProperty(field, out var value) || string.IsNullOrWhiteSpace(value.GetString())) throw new ArgumentException("Object destination requires " + field);
            if (!Te3Destinations.ObjectTypes.Contains(a.GetProperty("objectType").GetString())) throw new ArgumentException("Unsupported objectType.");
        }
        if (tool == "te3_capture" && (!a.TryGetProperty("savePath", out var path) || string.IsNullOrWhiteSpace(path.GetString()) || !Path.IsPathFullyQualified(path.GetString()!)))
            throw new ArgumentException("Absolute PNG savePath required.");
    }

    private static McpToolResult Json(object value) => TextResult(JsonSerializer.Serialize(value, McpProtocol.JsonOptions));

    // A successful write and a matched scene are not a visual review. A later
    // provider/display failure does not undo the write or establish a wrong scene.
    internal static object Assessment(bool sceneMatched, string? savedPath) => new {
        sceneIdentity = sceneMatched ? "matched" : "not-assessed",
        artifact = savedPath == null ? "not-saved" : "saved", path = savedPath,
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

    internal static CaptureFrame? FrameForWindow(System.Drawing.Rectangle bounds, System.Drawing.Rectangle workArea, bool maximized)
    {
        if (workArea.Contains(bounds)) return null;
        // Maximized Win32 bounds include invisible resize borders. Crop only when
        // the complete work area is enclosed and WindowPattern confirms maximized.
        if (!maximized || !bounds.Contains(workArea)) throw new InvalidOperationException("Capture window is clipped. Place it explicitly within one monitor before capture.");
        return new(bounds.Width, bounds.Height, workArea.X - bounds.X, workArea.Y - bounds.Y, workArea.Width, workArea.Height);
    }
}
