using System.Diagnostics;
using System.Text.Json;
using FlaUI.Mcp;
using FlaUI.Mcp.Core;

/// <summary>Attach-only TE3 navigation. The generic server remains application-independent and owns capture.</summary>
public sealed class Te3Companion(AutomationHost host, string name, Te3Interaction? interaction = null) : ToolBase
{
    public const string Navigate = "te3_navigate";

    // Never approved when none is passed, so a tool built without one refuses input.
    private readonly Te3Interaction grant = interaction ?? new Te3Interaction(() => 0);
    public const string ModelState = "te3_model_state";

    // Tools the page calls that only read; every other call is counted as input sent.
    private static readonly HashSet<string> ReadOnlyTools = ["windows_screenshot"];
    public static readonly string[] Names = [Navigate, ModelState];
    public override string Name => name;
    public override string Description =>
        name switch
        {
            Navigate =>
                "Navigate TE3 to a supported destination and verify arrival. Takes desktop focus; requires an active te3_interaction_begin grant, which the user's own keyboard or mouse input revokes. No model edits, script execution, or dialog dismissal.",
            // Spells out that it takes no focus: te3_navigate's description says it does, and an operator
            // reading only the tool names would otherwise gate both behind the same desktop handoff.
            ModelState =>
                "Read TE3's status bar: load state, Best Practice Analyzer count, caret position, selected object and connection state. Observation only - takes no desktop focus, sends no input, and changes nothing. Use it before a capture to learn whether the model can show what you need.",
            _ => throw new ArgumentException("Unknown companion tool."),
        };
    public override object InputSchema
    {
        get
        {
            if (name == ModelState)
            {
                return new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["processId"] = new
                        {
                            type = "integer",
                            minimum = 1,
                            description = "TabularEditor3 process ID. Optional when exactly one TE3 is running. Takes a process ID, not a window handle or element ref.",
                        },
                    },
                    additionalProperties = false,
                };
            }

            var properties = new Dictionary<string, object>
            {
                ["processId"] = new
                {
                    type = "integer",
                    minimum = 1,
                    description = "TabularEditor3 process ID. Optional when exactly one TE3 is running. Takes a process ID, not a window handle or element ref.",
                },
                ["destination"] = new { type = "string", @enum = Te3Destinations.Ids },
            };
            foreach (var key in new[] { "table", "objectName" })
            {
                properties[key] = new
                {
                    type = "string",
                    description = "For destination object; table and objectName required.",
                };
            }

            properties["folder"] = new
            {
                type = "string",
                description = "Display folder path of the object, backslash-separated; required when the object sits in a display folder.",
            };
            properties["objectType"] = new { type = "string", @enum = Te3Destinations.ObjectTypes };
            return new
            {
                type = "object",
                properties,
                required = new[] { "destination" },
                additionalProperties = false,
            };
        }
    }

    public override async Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var lastStep = "validate request";
        var phase = "validate";
        // Counted before each dispatch: an input that then fails may still have gone out.
        var invoked = 0;
        Te3Page? page = null;
        try
        {
            var a = arguments ?? JsonSerializer.SerializeToElement(new { });
            Validate(a, name);
            phase = "permission";
            // Navigation takes the desktop; a status-bar read sends no input and needs no grant.
            using var guard = name == Navigate ? grant.Guard() : null;
            phase = "attach";
            var (windows, hwnd, handle, identity) = Attach(a);

            async Task Invoke(string tool, object args)
            {
                identity.EnsureAlive();
                OperationContext.Check();
                if (!ReadOnlyTools.Contains(tool))
                {
                    invoked++;
                }

                var result = await host.Tools.ExecuteToolAsync(tool, JsonSerializer.SerializeToElement(args));
                if (result.IsError == true || result.Outcome?.IsPending == true)
                {
                    throw new InvalidOperationException(string.Join("; ", result.Content.Select(c => c.Text)));
                }
            }

            page = new Te3Page(host, handle, Invoke, step => lastStep = step);

            if (name == ModelState)
            {
                phase = "observe";
                lastStep = "read status bar";
                return ReadModelState(page, hwnd, phase, lastStep);
            }

            // Validate has already confirmed the id is in the registry.
            var destination = Te3Destinations.Find(GetStringArgument(a, "destination"))!;
            var hasPreferences = windows.Any(w => w.Title == "Preferences");
            RequireNoOtherWindow(windows, hwnd, destination.Id == "native-mcp-server" ? "MCP Server" : "Preferences");

            if (hasPreferences && destination.Section == null)
            {
                throw new InvalidOperationException(
                    "Preferences is open. Cancel it explicitly before navigating elsewhere."
                );
            }

            phase = "navigate";
            lastStep = "navigate " + destination.Id;
            await NavigateTo(page, destination, a, hasPreferences);

            lastStep = "verify " + destination.Id;
            phase = "verify-arrival";
            await page.VerifyCompanionDestination(
                destination,
                GetStringArgument(a, "table"),
                GetStringArgument(a, "objectName"),
                GetStringArgument(a, "objectType")
            );
            return Json(
                new
                {
                    assessment = Assessment(true),
                    destination = destination.Id,
                    phase,
                    lastStep,
                }
            );
        }
        catch (Exception ex)
        {
            return Failure(phase, lastStep, ex.Message, invoked + (page?.InputsSent ?? 0));
        }
    }

    // The TE3 process the request names, its one document window, and the input target for it.
    private (IReadOnlyList<Win32WindowInfo> Windows, nint Hwnd, string Handle, InputTarget Identity) Attach(
        JsonElement a
    )
    {
        using var process = Process.GetProcessById(ResolveProcessId(a, RunningTe3));
        if (!string.Equals(process.ProcessName, "TabularEditor3", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("processId must identify TabularEditor3.");
        }

        var windows = Win32Desktop.GetTopLevelWindows(process.Id);
        var hwnd = MainWindow(windows);
        var handle = host.Sessions.RegisterNativeWindow(hwnd, process.Id);
        var identity = host.Sessions.GetInputTarget(handle);
        if (host.Pending.TryGetPending(process.Id, out var pendingCall))
        {
            throw new InvalidOperationException(PendingInvokeTracker.DescribeBlocked(pendingCall));
        }

        return (windows, hwnd, handle, identity);
    }

    // No extra-window guard: an open dialog is exactly when a caller wants to read the status bar,
    // and this reads without touching anything.
    private static McpToolResult ReadModelState(Te3Page page, nint hwnd, string phase, string lastStep)
    {
        RequireRestored(hwnd);

        // Assessment(false): no scene was matched. This read a status bar, and claiming a
        // matched scene would assert a verification that never happened.
        return Json(
            new
            {
                assessment = Assessment(false),
                state = page.ObserveModelState(),
                phase,
                lastStep,
            }
        );
    }

    private async Task NavigateTo(Te3Page page, Te3Destination destination, JsonElement a, bool hasPreferences)
    {
        switch (destination)
        {
            case { Section: { } section }:
                if (!hasPreferences)
                {
                    await page.OpenPreferences();
                }

                await page.SearchPreferences(section);
                await page.SelectPreferencesSection(section);
                break;
            case { Id: "object" }:
                await page.SelectObjectPath(
                    GetStringArgument(a, "table")!,
                    GetStringArgument(a, "objectName")!,
                    GetStringArgument(a, "objectType")!,
                    GetStringArgument(a, "folder") ?? ""
                );
                break;
            case { Id: "tom-explorer" }:
                await page.OpenTom();
                break;
            case { Id: "tom-explorer/changes" }:
                await page.OpenTomChanges();
                break;
            case { Id: "native-mcp-server" }:
                await page.OpenNativeMcpServer();
                break;
            case { Id: "expression-editor" }:
                await page.SelectDocument("Expression Editor");
                break;
            default:
                throw new ArgumentException("No navigation for destination " + destination.Id);
        }
    }

    private static void RequireNoOtherWindow(IReadOnlyList<Win32WindowInfo> windows, nint main, string allowedDialog)
    {
        var unexpected = windows.Where(w => w.Hwnd != main && w.Title.Length > 0 && w.Title != allowedDialog).ToArray();
        if (unexpected.Length > 0)
        {
            throw new InvalidOperationException(
                "An additional TE3 window is open: "
                    + string.Join(
                        "; ",
                        unexpected.Select(w => $"{w.Title} (HWND {w.Hwnd}, tool window: {w.IsToolWindow})")
                    )
                    + ". Inspect it; no dialog was dismissed."
            );
        }
    }

    internal static McpToolResult Failure(string phase, string lastStep, string error, int inputsSent = 0)
    {
        var blocker = Blocker(error);
        var next = blocker switch
        {
            "activation-denied" =>
                "Windows refused to activate TE3; retrying will not help. The blocked step sent no input, but earlier steps may have. "
                    + "Ask the user to click the TE3 title bar, then check windows_list_windows before navigating again. In unattended runs, stop.",
            "desktop-unavailable" =>
                "No usable desktop session was observed. The blocked step sent no input, but earlier steps may have. "
                    + "Stop if the session is locked, disconnected or unattended.",
            _ => "Inspect state before continuing; no uncertain input is automatically replayed.",
        };
        return ErrorResult(
            JsonSerializer.Serialize(
                new
                {
                    assessment = Assessment(false),
                    phase,
                    lastStep,
                    error,
                    blocker,
                    next,
                    actionDispatched = inputsSent > 0,
                    inputsSent,
                },
                McpProtocol.JsonOptions
            )
        );
    }

    // Core's GuardedInput text, at the start of the error or after a tool's "Failed to ...: " prefix.
    // TODO(core): report activation-denied as a structured outcome instead of matching its text.
    private static readonly System.Text.RegularExpressions.Regex Blockers = new(
        "(?:^|: )(?<blocker>activation-denied): Windows kept |(?:^|: )(?<blocker>desktop-unavailable): Windows reports no foreground window",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant
    );

    private static string? Blocker(string error) =>
        Blockers.Match(error) is { Success: true } match ? match.Groups["blocker"].Value : null;

    internal static void Validate(JsonElement a, string tool)
    {
        if (a.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("Arguments must be an object.");
        }

        if (
            HasProcessId(a, out var pid)
            && (pid.ValueKind != JsonValueKind.Number || !pid.TryGetInt32(out var id) || id < 1)
        )
        {
            throw new ArgumentException("processId must be a positive integer.");
        }

        // A state read names no destination; requiring one would reject every valid call.
        if (tool == ModelState)
        {
            return;
        }

        if (!a.TryGetProperty("destination", out var d) || Te3Destinations.Find(d.GetString()) is not { } destination)
        {
            throw new ArgumentException(
                $"Unsupported destination; use one of: {string.Join(", ", Te3Destinations.Ids)}."
            );
        }

        if (destination.Id == "object")
        {
            ValidateObject(a);
        }
    }

    private static void ValidateObject(JsonElement a)
    {
        foreach (var field in new[] { "table", "objectName", "objectType" })
        {
            if (!a.TryGetProperty(field, out var value) || string.IsNullOrWhiteSpace(value.GetString()))
            {
                throw new ArgumentException("Object destination requires " + field);
            }
        }

        if (!Te3Destinations.ObjectTypes.Contains(a.GetProperty("objectType").GetString()))
        {
            throw new ArgumentException("Unsupported objectType.");
        }
    }

    private static bool HasProcessId(JsonElement a, out JsonElement pid) =>
        a.TryGetProperty("processId", out pid) && pid.ValueKind != JsonValueKind.Null;

    /// <summary>The caller's processId, or the only running TE3 when none was given.</summary>
    internal static int ResolveProcessId(JsonElement a, Func<int[]> running)
    {
        if (HasProcessId(a, out var pid))
        {
            return pid.GetInt32();
        }

        var ids = running();
        return ids.Length switch
        {
            1 => ids[0],
            0 => throw new InvalidOperationException("Tabular Editor 3 is not running."),
            _ => throw new ArgumentException(
                $"Several TE3 instances are running ({string.Join(", ", ids)}); pass processId."
            ),
        };
    }

    internal static int[] RunningTe3()
    {
        // Only this Windows session: another user's TE3 (fast user switching, RDP) is not ours to drive.
        using var current = Process.GetCurrentProcess();
        var session = current.SessionId;
        var processes = Process.GetProcessesByName("TabularEditor3");
        try
        {
            return processes.Where(p => p.SessionId == session).Select(p => p.Id).ToArray();
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    private static McpToolResult Json(object value) =>
        TextResult(JsonSerializer.Serialize(value, McpProtocol.JsonOptions));

    // A matched scene is not a visual review; the agent still has to look at its capture.
    internal static object Assessment(bool sceneMatched) =>
        new
        {
            sceneIdentity = sceneMatched ? "matched" : "not-assessed",
            visualCompleteness = "not-assessed",
            docsApproved = false,
        };

    private static void RequireRestored(nint hwnd)
    {
        if (Win32Desktop.IsMinimized(hwnd))
        {
            throw new InvalidOperationException(
                "TE3 is minimized, so its status bar is not exposed to UI Automation. "
                    + "Ask the user to restore the window; this tool will not restore it, because that takes focus."
            );
        }
    }

    internal static nint MainWindow(IReadOnlyList<Win32WindowInfo> windows)
    {
        // Process.MainWindowHandle can select an untitled popup after tree navigation.
        var main = windows
            .Where(w =>
                !w.IsToolWindow && !w.IsCloaked && w.Title.Contains("Tabular Editor 3", StringComparison.Ordinal)
            )
            .ToArray();
        if (main.Length != 1)
        {
            throw new InvalidOperationException(
                $"Expected one visible TE3 document window titled 'Tabular Editor 3', found {main.Length}; no launch or activation attempted."
            );
        }

        return main[0].Hwnd;
    }
}
