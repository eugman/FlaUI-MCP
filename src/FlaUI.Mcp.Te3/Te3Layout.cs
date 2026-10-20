using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Mcp;
using FlaUI.Mcp.Core;

/// <summary>
/// Shot layout for TE3: list, close and maximize docked panes and document tabs, and resize a pane
/// by dragging the splitter it shares with its neighbour. Every action goes through the guarded
/// core tools; nothing here edits the model.
/// </summary>
public sealed class Te3Layout(AutomationHost host) : ToolBase
{
    public const string ToolName = "te3_layout";
    private const int MaxCloses = 30;

    public override string Name => ToolName;
    public override string Description =>
        "Arrange TE3 for a screenshot. list: docked panes and document tabs with window-relative bounds. "
        + "close: close panes or tabs by title. keep: close every pane and tab except these titles (closing a "
        + "tab-group pane surfaces the next tab, so this repeats until only the kept ones remain). "
        + "maximize-pane: the pane's own Maximize button. resize: drag the splitter a pane shares with its "
        + "neighbour until the pane is `width` or `height` physical pixels. Takes desktop focus; no model edits.";
    public override object InputSchema =>
        new
        {
            type = "object",
            properties = new
            {
                processId = new { type = "integer", minimum = 1 },
                action = new { type = "string", @enum = new[] { "list", "close", "keep", "maximize-pane", "resize" } },
                titles = new
                {
                    type = "array",
                    items = new { type = "string" },
                    description = "close/keep: pane titles or document tab names, exactly as list shows them.",
                },
                title = new { type = "string", description = "maximize-pane/resize: the pane title." },
                width = new { type = "integer", description = "resize: target width in physical pixels." },
                height = new { type = "integer", description = "resize: target height in physical pixels." },
            },
            required = new[] { "action" },
            additionalProperties = false,
        };

    private sealed record Part(string Title, string Kind, AutomationElement Close, AutomationElement Container);

    public override async Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        try
        {
            var a = arguments ?? JsonSerializer.SerializeToElement(new { });
            using var process = Process.GetProcessById(Te3Companion.ResolveProcessId(a, Te3Companion.RunningTe3));
            var hwnd = Te3Companion.MainWindow(Win32Desktop.GetTopLevelWindows(process.Id));
            var handle = host.Sessions.RegisterNativeWindow(hwnd, process.Id);
            var window =
                host.Sessions.GetWindow(handle) ?? throw new InvalidOperationException("TE3 window not readable.");
            object result = GetStringArgument(a, "action") switch
            {
                "list" => List(window),
                "close" => await Close(handle, window, Titles(a), keep: false),
                "keep" => await Close(handle, window, Titles(a), keep: true),
                "maximize-pane" => await MaximizePane(handle, window, Required(a, "title")),
                "resize" => await Resize(handle, window, Required(a, "title"), a),
                _ => throw new ArgumentException("action must be list, close, keep, maximize-pane or resize"),
            };
            return TextResult(JsonSerializer.Serialize(result, McpProtocol.JsonOptions));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or TimeoutException)
        {
            return ErrorResult($"{ToolName}: {ex.Message}");
        }
    }

    private string Required(JsonElement a, string name) =>
        GetStringArgument(a, name) ?? throw new ArgumentException($"{name} is required for this action.");

    private static string[] Titles(JsonElement a) =>
        a.TryGetProperty("titles", out var t) && t.ValueKind == JsonValueKind.Array
            ? t.EnumerateArray().Select(x => x.GetString() ?? "").ToArray()
            : throw new ArgumentException("titles is required for close and keep.");

    /// <summary>Docked panes (a TitleBar with a Close button inside the window) and closable document tabs.</summary>
    private static List<Part> Parts(Window window)
    {
        var parts = new List<Part>();
        foreach (var bar in window.FindAllDescendants(cf => cf.ByControlType(ControlType.TitleBar)))
        {
            // The window's own title bars are direct children of it; a dock pane's sits one level down.
            if (bar.Parent is not { } container || container.ControlType == ControlType.Window)
            {
                continue;
            }

            if (CloseButton(bar) is { } close)
            {
                parts.Add(new Part(bar.Name, "pane", close, container));
            }
        }

        foreach (var tab in window.FindAllDescendants(cf => cf.ByControlType(ControlType.TabItem)))
        {
            if (CloseButton(tab) is { } close && tab.Parent is { } strip)
            {
                parts.Add(new Part(tab.Name, "document", close, strip.Parent ?? strip));
            }
        }

        return parts;
    }

    private static AutomationElement? CloseButton(AutomationElement owner) =>
        owner.FindFirstChild(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("Close")));

    private static object List(Window window)
    {
        var origin = window.BoundingRectangle;
        return Parts(window)
            .Select(p => new
            {
                p.Title,
                p.Kind,
                bounds = Relative(p.Container.BoundingRectangle, origin),
            })
            .ToList();
    }

    private static object Relative(Rectangle r, Rectangle origin) =>
        new
        {
            x = r.X - origin.X,
            y = r.Y - origin.Y,
            width = r.Width,
            height = r.Height,
        };

    private async Task<object> Close(string handle, Window window, string[] titles, bool keep)
    {
        var closed = new List<string>();
        var attempted = new HashSet<string>();
        for (var i = 0; i < MaxCloses; i++)
        {
            var next = Parts(window).FirstOrDefault(p => titles.Contains(p.Title) != keep && attempted.Add(p.Title));
            if (next == null)
            {
                break;
            }

            await Click(handle, next.Close);
            RequireNoDialog(window, next.Title);
            closed.Add(next.Title);
        }

        var remaining = Parts(window).Select(p => p.Title).ToList();
        var unresolved = keep ? titles.Except(remaining).ToList() : titles.Intersect(remaining).ToList();
        return new
        {
            closed,
            remaining,
            unresolved,
        };
    }

    // Closing an unsaved document asks to save. Stop there: answering it is the caller's decision.
    private static void RequireNoDialog(Window window, string closing)
    {
        var pid = window.Properties.ProcessId.ValueOrDefault;
        var main = window.Properties.NativeWindowHandle.ValueOrDefault;
        var dialog = Win32Desktop
            .GetTopLevelWindows(pid)
            .FirstOrDefault(w => w.Hwnd != main && w.Title.Length > 0 && !w.IsToolWindow);
        if (dialog != null)
        {
            throw new InvalidOperationException(
                $"Closing '{closing}' opened \"{dialog.Title}\"; nothing else was closed. Answer it, then run keep/close again."
            );
        }
    }

    private async Task<object> MaximizePane(string handle, Window window, string title)
    {
        var pane =
            Parts(window).FirstOrDefault(p => p.Kind == "pane" && p.Title == title)
            ?? throw new ArgumentException($"No docked pane titled '{title}'; use action list.");
        var maximize =
            pane.Close.Parent?.FindFirstChild(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("Maximize")))
            ?? throw new InvalidOperationException(
                $"Pane '{title}' has no Maximize button (a tab-group pane may not)."
            );
        await Click(handle, maximize);
        return new { maximized = title };
    }

    private async Task<object> Resize(string handle, Window window, string title, JsonElement a)
    {
        var parts = Parts(window);
        var pane =
            parts.FirstOrDefault(p => p.Kind == "pane" && p.Title == title)
            ?? throw new ArgumentException($"No docked pane titled '{title}'; use action list.");
        var before = pane.Container.BoundingRectangle;
        var others = parts.Where(p => p != pane).Select(p => p.Container.BoundingRectangle).Distinct();
        var (from, to) = SplitterPlan.Resize(
            before,
            others,
            GetArgument<int?>(a, "width"),
            GetArgument<int?>(a, "height")
        );
        var origin = window.BoundingRectangle;
        await Tool(
            "windows_mouse",
            new
            {
                action = "drag",
                handle,
                from = new { x = from.X - origin.X, y = from.Y - origin.Y },
                to = new { x = to.X - origin.X, y = to.Y - origin.Y },
            }
        );
        var after = pane.Container.BoundingRectangle;
        return new
        {
            title,
            before = Relative(before, origin),
            after = Relative(after, origin),
        };
    }

    private Task Click(string handle, AutomationElement element) =>
        Tool("windows_click", new { @ref = host.Elements.Register(handle, element), handle });

    private async Task Tool(string tool, object args)
    {
        OperationContext.Check();
        var result = await host.Tools.ExecuteToolAsync(tool, JsonSerializer.SerializeToElement(args));
        if (result.IsError == true || result.Outcome?.IsPending == true)
        {
            throw new InvalidOperationException(string.Join("; ", result.Content.Select(c => c.Text)));
        }
    }
}
