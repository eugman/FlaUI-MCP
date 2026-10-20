using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Mcp;
using FlaUI.Mcp.Core;
using FlaUI.Mcp.Te3;

/// <summary>
/// Shot layout for TE3: list panes and document tabs, hide docked panes, and resize a pane
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
        + "close: hide named docked panes by title; document tabs are never closed. keep: hide every docked pane except these titles (closing a "
        + "tab-group pane surfaces the next tab, so this repeats until only the kept ones remain). "
        + "show-pane / hide-pane: named AI Assistant, TOM Explorer, or Properties dock surfaces; show refuses absent panes until their reopen locator is verified. "
        + "plan-preset / apply-preset: assistant-model-review 1920x1080 layout. "
        + "maximize-pane / restore-pane: the pane's own Maximize or Restore button (TE3 remembers a maximized "
        + "pane across restarts, so restore it when done). Pane titles match with or without TE3's \"(Changed)\" "
        + "suffix. resize: drag the splitter a pane shares with its "
        + "neighbour until the pane is `width` or `height` physical pixels. Takes desktop focus; no model edits.";
    public override object InputSchema =>
        new
        {
            type = "object",
            properties = new
            {
                processId = new { type = "integer", minimum = 1 },
                action = new
                {
                    type = "string",
                    @enum = new[]
                    {
                        "list",
                        "close",
                        "keep",
                        "show-pane",
                        "hide-pane",
                        "plan-preset",
                        "apply-preset",
                        "maximize-pane",
                        "restore-pane",
                        "resize",
                    },
                },
                titles = new
                {
                    type = "array",
                    items = new { type = "string" },
                    description = "close/keep: docked pane titles, exactly as list shows them; document tabs are preserved.",
                },
                title = new
                {
                    type = "string",
                    description = "show-pane/hide-pane/maximize-pane/restore-pane/resize: the pane title.",
                },
                preset = new { type = "string", @enum = new[] { Te3PanePlan.AssistantModelReview } },
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
                "show-pane" => ShowPane(window, Required(a, "title")),
                "hide-pane" => await HidePane(handle, window, Required(a, "title")),
                "plan-preset" => Te3PanePlan.Resolve(
                    Required(a, "preset"),
                    window.BoundingRectangle.Width,
                    window.BoundingRectangle.Height
                ),
                "apply-preset" => await ApplyPreset(handle, window, Required(a, "preset")),
                "maximize-pane" => await PaneButton(handle, window, Required(a, "title"), "Maximize"),
                "restore-pane" => await PaneButton(handle, window, Required(a, "title"), "Restore"),
                "resize" => await Resize(handle, window, Required(a, "title"), a),
                _ => throw new ArgumentException("Unsupported TE3 layout action"),
            };
            return TextResult(JsonSerializer.Serialize(result, McpProtocol.JsonOptions));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or TimeoutException)
        {
            return ErrorResult($"{ToolName}: {ex.Message}");
        }
    }

    private static object ShowPane(Window window, string title)
    {
        if (!Te3PanePlan.IsNamedPane(title))
        {
            throw new ArgumentException("show-pane accepts AI Assistant, TOM Explorer or Properties.");
        }

        if (Parts(window).Any(part => part.Kind == "pane" && TitleMatches(part.Title, title)))
        {
            return new
            {
                pane = title,
                visible = true,
                changed = false,
            };
        }

        throw new InvalidOperationException(
            "Pane is hidden. Its reopen menu locator is not verified for this TE3 build; no input sent."
        );
    }

    private async Task<object> HidePane(string handle, Window window, string title)
    {
        if (!Te3PanePlan.IsNamedPane(title))
        {
            throw new ArgumentException("hide-pane accepts AI Assistant, TOM Explorer or Properties.");
        }

        var pane = Parts(window).FirstOrDefault(part => part.Kind == "pane" && TitleMatches(part.Title, title));
        if (pane == null)
        {
            return new
            {
                pane = title,
                visible = false,
                changed = false,
            };
        }

        await Click(handle, pane.Close);
        await RequireNoDialog(window, pane.Title);
        if (Parts(window).Any(part => part.Kind == "pane" && TitleMatches(part.Title, title)))
        {
            throw new InvalidOperationException("Pane remained visible after Close; inspect before retrying.");
        }

        return new
        {
            pane = title,
            visible = false,
            changed = true,
        };
    }

    private async Task<object> ApplyPreset(string handle, Window window, string preset)
    {
        var sizes = Te3PanePlan.Resolve(preset, window.BoundingRectangle.Width, window.BoundingRectangle.Height);
        var parts = Parts(window);
        foreach (var size in sizes)
        {
            if (!parts.Any(part => part.Kind == "pane" && TitleMatches(part.Title, size.Pane)))
            {
                throw new InvalidOperationException("Preset needs visible pane '" + size.Pane + "'; no resize sent.");
            }
        }

        var changes = new List<object>();
        foreach (var size in sizes)
        {
            var args = JsonSerializer.SerializeToElement(new { width = size.Width, height = size.Height });
            changes.Add(await Resize(handle, window, size.Pane, args, strict: true));
        }

        return new { preset, changes };
    }

    private string Required(JsonElement a, string name) =>
        GetStringArgument(a, name) ?? throw new ArgumentException($"{name} is required for this action.");

    private static string[] Titles(JsonElement a)
    {
        var titles =
            a.TryGetProperty("titles", out var t) && t.ValueKind == JsonValueKind.Array
                ? t.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToArray()
                : throw new ArgumentException("titles is required for close and keep.");
        // An empty keep would close every pane and tab.
        return titles.Length > 0 ? titles : throw new ArgumentException("titles must name at least one pane or tab.");
    }

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
            var next = Parts(window)
                .FirstOrDefault(p =>
                    CanCloseKind(p.Kind) && titles.Any(t => TitleMatches(p.Title, t)) != keep && attempted.Add(p.Title)
                );
            if (next == null)
            {
                break;
            }

            await Click(handle, next.Close);
            await RequireNoDialog(window, next.Title);
            closed.Add(next.Title);
        }

        var remaining = Parts(window).Where(p => p.Kind == "pane").Select(p => p.Title).ToList();
        var unresolved = titles.Where(t => remaining.Any(r => TitleMatches(r, t)) != keep).ToList();
        return new
        {
            closed,
            remaining,
            unresolved,
        };
    }

    internal static bool CanCloseKind(string kind) => kind == "pane";

    // Closing a pane can open a dialog on an unfamiliar build. Watch for ~1s and
    // stop there: answering it is the caller's decision.
    private static async Task RequireNoDialog(Window window, string closing)
    {
        var pid = window.Properties.ProcessId.ValueOrDefault;
        var main = window.Properties.NativeWindowHandle.ValueOrDefault;
        for (var waited = 0; waited <= 1000; waited += 200)
        {
            var dialog = Win32Desktop
                .GetTopLevelWindows(pid)
                .FirstOrDefault(w => w.Hwnd != main && w.Title.Length > 0 && !w.IsToolWindow);
            if (dialog != null)
            {
                throw new InvalidOperationException(
                    $"Closing '{closing}' opened \"{dialog.Title}\"; nothing else was closed. Answer it, then run keep/close again."
                );
            }

            await Task.Delay(200);
        }
    }

    // TE3 retitles a pane while its Show changes filter is on ("TOM Explorer (Changed)").
    private const string ChangedSuffix = " (Changed)";

    /// <summary>A pane title matches the requested one exactly, or once TE3's "(Changed)" suffix is ignored.</summary>
    internal static bool TitleMatches(string paneTitle, string requested) =>
        paneTitle == requested || BaseTitle(paneTitle) == BaseTitle(requested);

    private static string BaseTitle(string title) =>
        title.EndsWith(ChangedSuffix, StringComparison.Ordinal) ? title[..^ChangedSuffix.Length] : title;

    private static Part FindPane(Window window, string title) =>
        Parts(window).FirstOrDefault(p => p.Kind == "pane" && TitleMatches(p.Title, title))
        ?? throw new ArgumentException($"No docked pane titled '{title}'; use action list.");

    // A docked pane shows Maximize; once maximized the same button is named Restore (observed on 3.27.1).
    private async Task<object> PaneButton(string handle, Window window, string title, string button)
    {
        var pane = FindPane(window, title);
        var target =
            pane.Close.Parent?.FindFirstChild(cf => cf.ByControlType(ControlType.Button).And(cf.ByName(button)))
            ?? throw new InvalidOperationException(
                button == "Maximize"
                    ? $"Pane '{pane.Title}' has no Maximize button (a tab-group pane may not, or it is already maximized)."
                    : $"Pane '{pane.Title}' has no Restore button; it is not maximized."
            );
        await Click(handle, target);
        return button == "Maximize" ? new { maximized = pane.Title } : (object)new { restored = pane.Title };
    }

    private async Task<object> Resize(string handle, Window window, string title, JsonElement a, bool strict = false)
    {
        var parts = Parts(window);
        var pane = FindPane(window, title);
        var before = pane.Container.BoundingRectangle;
        var others = parts.Where(p => p.Title != pane.Title).Select(p => p.Container.BoundingRectangle).Distinct();
        var (from, to) = SplitterPlan.Resize(
            before,
            others,
            GetArgument<int?>(a, "width"),
            GetArgument<int?>(a, "height"),
            window.BoundingRectangle
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
        await Task.Delay(300);
        var after = FindPane(window, title).Container.BoundingRectangle;
        var requested = GetArgument<int?>(a, "width") ?? GetArgument<int?>(a, "height");
        var reached = GetArgument<int?>(a, "width") is null ? after.Height : after.Width;
        if (strict && Math.Abs(reached - requested!.Value) > 4)
        {
            throw new InvalidOperationException(
                "Preset resize did not reach the requested size; inspect before retrying."
            );
        }

        return new
        {
            title,
            before = Relative(before, origin),
            after = Relative(after, origin),
            requested,
            reached,
            note = Math.Abs(reached - requested!.Value) > 4
                ? "The pane did not reach the requested size; the drag may have missed the splitter."
                : null,
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
