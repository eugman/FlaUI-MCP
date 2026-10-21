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
/// core tools; nothing here edits the model. Two tools, so one annotation describes each:
/// te3_layout_read only reads (list, show-pane, plan-preset), and te3_layout changes the layout.
/// </summary>
/// <param name="readOnly">True for te3_layout_read.</param>
public sealed partial class Te3Layout(AutomationHost host, Te3Interaction? interaction = null, bool readOnly = false)
    : ToolBase
{
    public const string ToolName = "te3_layout";
    public const string ReadToolName = "te3_layout_read";

    private const int MaxCloses = 30;

    // Never approved when none is passed, so a tool built without one refuses input.
    private readonly Te3Interaction grant = interaction ?? new Te3Interaction(() => 0);

    public override string Name => readOnly ? ReadToolName : ToolName;

    public override McpToolAnnotations Annotations =>
        readOnly ? McpToolAnnotations.ReadOnly : McpToolAnnotations.Input(destructive: true);

    private sealed record Part(string Title, string Kind, AutomationElement Close, AutomationElement Container);

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments) =>
        Respond(inputs => Run(arguments, inputs));

    private async Task<McpToolResult> Run(JsonElement? arguments, InputTally inputs)
    {
        var a = arguments ?? JsonSerializer.SerializeToElement(new { });
        RequireOwnAction(GetStringArgument(a, "action"));
        if (GetStringArgument(a, "action") == "plan-preset")
        {
            var plan = Te3PanePlan.Resolve(
                Required(a, "preset"),
                GetArgument<int?>(a, "windowWidth") ?? 1920,
                GetArgument<int?>(a, "windowHeight") ?? 1080
            );
            return TextResult(JsonSerializer.Serialize(plan, McpProtocol.JsonOptions));
        }

        using var guard = readOnly ? null : grant.Guard();
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
            "reopen-pane" => await ReopenPane(inputs, handle, window, Required(a, "title")),
            "hide-pane" => await HidePane(inputs, handle, window, Required(a, "title")),
            "apply-preset" => await ApplyPreset(inputs, handle, window, Required(a, "preset")),
            "maximize-pane" => await PaneButton(inputs, handle, window, Required(a, "title"), "Maximize"),
            "restore-pane" => await PaneButton(inputs, handle, window, Required(a, "title"), "Restore"),
            "resize" => await Resize(inputs, handle, window, Required(a, "title"), a),
            _ => throw new ArgumentException("Unsupported TE3 layout action"),
        };
        return TextResult(JsonSerializer.Serialize(result, McpProtocol.JsonOptions));
    }

    internal static string DialogOpened(string closing, string dialog) => LayoutSteps.DialogOpened(closing, dialog);

    internal static Task<McpToolResult> Respond(Func<InputTally, Task<McpToolResult>> run) =>
        LayoutSteps.Respond(ToolName, run);

    internal static McpToolResult Failure(Exception ex, int inputsSent = 0) =>
        LayoutSteps.Failure(ToolName, ex, inputsSent);

    /// <summary>Registers the TE3 window for the raw descendant refs this tool clicks.</summary>
    internal static string Bind(AutomationHost host, nint hwnd, int processId)
    {
        var handle = host.Sessions.RegisterNativeWindow(hwnd, processId);
        // Parts come from FindAllDescendants, which bypasses ElementQuery, the step that otherwise
        // records this identity before refs reach GuardedInput and the pending-provider check. Core
        // now refuses a mutation through a ref without one ("no process identity; no action
        // dispatched"), so without this every layout click would be refused rather than unguarded.
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

        throw new InvalidOperationException(LayoutSteps.HiddenMessage(title));
    }

    private Task<object> ReopenPane(InputTally inputs, string handle, Window window, string title) =>
        LayoutSteps.Reopen(host, handle, window, inputs, title);

    internal static bool PaneVisible(Window window, string title) =>
        Parts(window).Any(part => part.Kind == "pane" && TitleMatches(part.Title, title));

    internal static bool DocumentOpen(Window window, string title) =>
        Parts(window).Any(part => part.Kind == "document" && TitleMatches(part.Title, title));

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
        await LayoutSteps.RequireNoDialog(window, pane.Title);
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
        if (keep)
        {
            LayoutChecks.RequireKept(titles, Parts(window).Select(p => p.Title));
        }

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
                await LayoutSteps.RequireNoDialog(window, next.Title);
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
        // Maximize and Restore are one button renamed, so the other name showing is the read-back.
        var other = button == "Maximize" ? "Restore" : "Maximize";
        await LayoutChecks.ConfirmPaneButton(
            pane.Title,
            button,
            () => PaneButtonShown(window, title, other),
            ms => Task.Delay(ms)
        );
        return button == "Maximize"
            ? new { maximized = pane.Title, readBack = other }
            : (object)new { restored = pane.Title, readBack = other };
    }

    private static bool PaneButtonShown(Window window, string title, string button) =>
        Parts(window)
            .FirstOrDefault(p => p.Kind == "pane" && TitleMatches(p.Title, title))
            ?.Close.Parent?.FindFirstChild(cf => cf.ByControlType(ControlType.Button).And(cf.ByName(button)))
            is not null;

    private Task Click(InputTally inputs, string handle, AutomationElement element) =>
        Tool(inputs, "windows_click", new { @ref = host.Elements.Register(handle, element), handle });

    private Task Tool(InputTally inputs, string tool, object args) =>
        inputs.Invoke(() => host.Tools.ExecuteToolAsync(tool, JsonSerializer.SerializeToElement(args)), tool: tool);
}
