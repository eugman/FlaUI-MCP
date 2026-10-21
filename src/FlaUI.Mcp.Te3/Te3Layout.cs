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
public sealed class Te3Layout(AutomationHost host, Te3Interaction? interaction = null) : ToolBase
{
    public const string ToolName = "te3_layout";

    private const int MaxCloses = 30;

    // Never approved when none is passed, so a tool built without one refuses input.
    private readonly Te3Interaction grant = interaction ?? new Te3Interaction(() => 0);

    public override string Name => ToolName;
    public override string Description =>
        "Arrange TE3 for a screenshot. list: docked panes and document tabs with window-relative bounds. "
        + "close: hide named docked panes by title; document tabs are never closed. keep: hide every docked pane except these titles (closing a "
        + "tab-group pane surfaces the next tab, so this repeats until only the kept ones remain). "
        + "show-pane / hide-pane: named AI Assistant, TOM Explorer, or Properties dock surfaces; show refuses absent panes until their reopen locator is verified. "
        + "plan-preset: offline assistant-model-review geometry (windowWidth/windowHeight default to 1920x1080). apply-preset: live guarded resizing. "
        + "maximize-pane / restore-pane: the pane's own Maximize or Restore button (TE3 remembers a maximized "
        + "pane across restarts, so restore it when done). Pane titles match with or without TE3's \"(Changed)\" "
        + "suffix. resize: drag the splitter a pane shares with its "
        + "neighbour until the pane is `width` or `height` physical pixels. Changes take desktop focus and need an active "
        + "te3_interaction_begin grant; list, plan-preset and show-pane send no input. No model edits.";
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
                windowWidth = new
                {
                    type = "integer",
                    minimum = 1,
                    description = "plan-preset: physical window width; defaults to 1920.",
                },
                windowHeight = new
                {
                    type = "integer",
                    minimum = 1,
                    description = "plan-preset: physical window height; defaults to 1080.",
                },
                width = new { type = "integer", description = "resize: target width in physical pixels." },
                height = new { type = "integer", description = "resize: target height in physical pixels." },
            },
            required = new[] { "action" },
            additionalProperties = false,
        };

    private sealed record Part(string Title, string Kind, AutomationElement Close, AutomationElement Container);

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments) =>
        Respond(inputs => Run(arguments, inputs));

    private async Task<McpToolResult> Run(JsonElement? arguments, InputTally inputs)
    {
        var a = arguments ?? JsonSerializer.SerializeToElement(new { });
        if (GetStringArgument(a, "action") == "plan-preset")
        {
            var plan = Te3PanePlan.Resolve(
                Required(a, "preset"),
                GetArgument<int?>(a, "windowWidth") ?? 1920,
                GetArgument<int?>(a, "windowHeight") ?? 1080
            );
            return TextResult(JsonSerializer.Serialize(plan, McpProtocol.JsonOptions));
        }

        using var guard = GetStringArgument(a, "action") is "list" or "show-pane" ? null : grant.Guard();
        var processId = Te3Companion.ResolveProcessId(a, Te3Companion.RunningTe3);
        var hwnd = Te3Companion.MainWindow(Win32Desktop.GetTopLevelWindows(processId));
        var handle = Bind(host, hwnd, processId);
        var window = host.Sessions.GetWindow(handle) ?? throw new InvalidOperationException("TE3 window not readable.");
        object result = GetStringArgument(a, "action") switch
        {
            "list" => List(window),
            "close" => await Close(inputs, handle, window, Titles(a), keep: false),
            "keep" => await Close(inputs, handle, window, Titles(a), keep: true),
            "show-pane" => ShowPane(window, Required(a, "title")),
            "hide-pane" => await HidePane(inputs, handle, window, Required(a, "title")),
            "apply-preset" => await ApplyPreset(inputs, handle, window, Required(a, "preset")),
            "maximize-pane" => await PaneButton(inputs, handle, window, Required(a, "title"), "Maximize"),
            "restore-pane" => await PaneButton(inputs, handle, window, Required(a, "title"), "Restore"),
            "resize" => await Resize(inputs, handle, window, Required(a, "title"), a),
            _ => throw new ArgumentException("Unsupported TE3 layout action"),
        };
        return TextResult(JsonSerializer.Serialize(result, McpProtocol.JsonOptions));
    }

    internal static Task<McpToolResult> Respond(Func<InputTally, Task<McpToolResult>> run) =>
        LayoutSteps.Respond(ToolName, run);

    internal static McpToolResult Failure(Exception ex, int inputsSent = 0) =>
        LayoutSteps.Failure(ToolName, ex, inputsSent);

    /// <summary>Registers the TE3 window for the raw descendant refs this tool clicks.</summary>
    internal static string Bind(AutomationHost host, nint hwnd, int processId)
    {
        var handle = host.Sessions.RegisterNativeWindow(hwnd, processId);
        // Parts come from FindAllDescendants, which bypasses ElementQuery, the step that otherwise
        // records this identity before refs reach GuardedInput and the pending-provider check.
        // TODO(core): ElementRegistry.Register should refuse a handle with no identity.
        host.Elements.SetWindowIdentity(handle, processId, hwnd);
        return handle;
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

    private async Task<object> HidePane(InputTally inputs, string handle, Window window, string title)
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

        await Click(inputs, handle, pane.Close);
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

    private async Task<object> ApplyPreset(InputTally inputs, string handle, Window window, string preset)
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

        var next = 0;
        var changes = await LayoutSteps.Progress(
            "changes",
            async sent =>
            {
                if (next == sizes.Length)
                {
                    return null;
                }

                var size = sizes[next++];
                var args = JsonSerializer.SerializeToElement(new { width = size.Width, height = size.Height });
                object? changed = null;
                await LayoutSteps.Sending(
                    inputs,
                    sent,
                    size.Pane,
                    async () => changed = await Resize(inputs, handle, window, size.Pane, args, strict: true)
                );
                return changed;
            }
        );
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

    private async Task<object> Close(InputTally inputs, string handle, Window window, string[] titles, bool keep)
    {
        var attempted = new HashSet<string>();
        var closes = 0;
        var closed = await LayoutSteps.Progress(
            "closed",
            async sent =>
            {
                var next =
                    closes++ < MaxCloses
                        ? Parts(window)
                            .FirstOrDefault(p => ShouldClose(p, titles, keep) && !attempted.Contains(p.Title))
                        : null;
                if (next == null)
                {
                    return null;
                }

                attempted.Add(next.Title);
                await LayoutSteps.Sending(inputs, sent, next.Title, () => Click(inputs, handle, next.Close));
                await RequireNoDialog(window, next.Title);
                return next.Title;
            }
        );

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

    private static bool ShouldClose(Part part, string[] titles, bool keep) =>
        CanCloseKind(part.Kind) && titles.Any(t => TitleMatches(part.Title, t)) != keep;

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
                throw new InvalidOperationException(DialogOpened(closing, dialog.Title));
            }

            await Task.Delay(200);
        }
    }

    internal static string DialogOpened(string closing, string dialog) =>
        $"Closing '{closing}' opened \"{dialog}\". Answer it, then run keep/close again.";

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
    private async Task<object> PaneButton(InputTally inputs, string handle, Window window, string title, string button)
    {
        var pane = FindPane(window, title);
        var target =
            pane.Close.Parent?.FindFirstChild(cf => cf.ByControlType(ControlType.Button).And(cf.ByName(button)))
            ?? throw new InvalidOperationException(
                button == "Maximize"
                    ? $"Pane '{pane.Title}' has no Maximize button (a tab-group pane may not, or it is already maximized)."
                    : $"Pane '{pane.Title}' has no Restore button; it is not maximized."
            );
        await Click(inputs, handle, target);
        return button == "Maximize" ? new { maximized = pane.Title } : (object)new { restored = pane.Title };
    }

    private async Task<object> Resize(
        InputTally inputs,
        string handle,
        Window window,
        string title,
        JsonElement a,
        bool strict = false
    )
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
            inputs,
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

    private Task Click(InputTally inputs, string handle, AutomationElement element) =>
        Tool(inputs, "windows_click", new { @ref = host.Elements.Register(handle, element), handle });

    private Task Tool(InputTally inputs, string tool, object args) =>
        inputs.Invoke(() => host.Tools.ExecuteToolAsync(tool, JsonSerializer.SerializeToElement(args)), tool: tool);
}
