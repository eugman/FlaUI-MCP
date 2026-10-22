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
/// <param name="attach">The layout for a call; null attaches to the running TE3. Tests pass a scripted one.</param>
/// <param name="settings">TE3's settings folder for snapshot and restore; null is the user's. Tests pass their own.</param>
/// <param name="running">The running TE3 process IDs; null reads them. Tests pass their own.</param>
/// <param name="snapshots">The folder snapshots must stay under; null is <see cref="LayoutFiles.DefaultRoot"/>.</param>
public sealed partial class Te3Layout(
    AutomationHost host,
    Te3Interaction? interaction = null,
    bool readOnly = false,
    Func<JsonElement, InputTally, ILayoutSurface>? attach = null,
    string? settings = null,
    Func<int[]>? running = null,
    string? snapshots = null
) : ToolBase
{
    public const string ToolName = "te3_layout";
    public const string ReadToolName = "te3_layout_read";

    private const int MaxCloses = 30;

    // Never approved when none is passed, so a tool built without one refuses input.
    private readonly Te3Interaction grant = interaction ?? Te3Interaction.Refusing();

    public override string Name => readOnly ? ReadToolName : ToolName;

    public override McpToolAnnotations Annotations =>
        readOnly ? McpToolAnnotations.ReadOnly : McpToolAnnotations.Input(destructive: true);

    private sealed record Part(string Title, string Kind, AutomationElement Close, AutomationElement Container);

    // A failure after input pauses the grant; a read of the layout (list, show-pane) lifts it. An exception Respond
    // does not turn into a reply escapes after input may have gone out, so it pauses too.
    public override async Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var settled = false;
        try
        {
            var result = FailurePause.After(
                grant,
                await Respond(inputs => Run(arguments, inputs)),
                observation: readOnly && Observes(arguments)
            );
            settled = true;
            return result;
        }
        finally
        {
            if (!settled && !readOnly)
            {
                grant.Pause("a te3_layout call failed with an unexpected error, so input may have gone out");
            }
        }
    }

    private bool Observes(JsonElement? arguments) =>
        arguments is { } a && GetStringArgument(a, "action") is "list" or "show-pane";

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

        // Files only, no desktop: no grant (a restore still refuses while TE3 runs).
        if (GetStringArgument(a, "action") is "snapshot" or "restore")
        {
            return Proven(GetStringArgument(a, "action")!, SettingsFiles(a));
        }

        using var guard = readOnly ? null : grant.Guard();

        var (surface, handle, window) = Attach(a, inputs);
        object result = GetStringArgument(a, "action") switch
        {
            "list" => List(surface),
            "close" => await Close(surface, inputs, Titles(a), keep: false),
            "keep" => await Close(surface, inputs, Titles(a), keep: true),
            "show-pane" => ShowPane(surface, Required(a, "title")),
            "reopen-pane" => await ReopenPane(inputs, handle, Live(window), Required(a, "title")),
            "hide-pane" => await HidePane(surface, Required(a, "title")),
            "apply-preset" => await ApplyPreset(inputs, handle, Live(window), Required(a, "preset")),
            "maximize-pane" => await PaneButton(surface, Required(a, "title"), "Maximize"),
            "restore-pane" => await PaneButton(surface, Required(a, "title"), "Restore"),
            "resize" => await Resize(inputs, handle, Live(window), Required(a, "title"), a),
            "close-document" => await CloseDocument(surface, Required(a, "title")),
            _ => throw new ArgumentException("Unsupported TE3 layout action"),
        };
        return readOnly
            ? TextResult(JsonSerializer.Serialize(result, McpProtocol.JsonOptions))
            : Proven(GetStringArgument(a, "action")!, result);
    }

    // A change's reply with the proof of its read-back (LayoutProof), so its status is ok only when verified.
    private McpToolResult Proven(string action, object result) =>
        LayoutProof.Attach(
            TextResult(JsonSerializer.Serialize(result, McpProtocol.JsonOptions)),
            LayoutProof.For(action, JsonSerializer.SerializeToElement(result, McpProtocol.JsonOptions))
        );

    // The running TE3's window, or the scripted layout a test passed (which has no window for the drag actions).
    private (ILayoutSurface Surface, string Handle, Window? Window) Attach(JsonElement a, InputTally inputs)
    {
        if (attach != null)
        {
            return (attach(a, inputs), "", null);
        }

        var processId = Te3Companion.ResolveProcessId(a, Te3Companion.RunningTe3);
        var hwnd = Te3Companion.MainWindow(Win32Desktop.GetTopLevelWindows(processId));
        var handle = Bind(host, hwnd, processId);
        var window = host.Sessions.GetWindow(handle) ?? throw new InvalidOperationException("TE3 window not readable.");
        return (new UiaSurface(this, inputs, handle, window), handle, window);
    }

    // snapshot / restore: TE3's layout files, with no TE3 window needed (see LayoutFiles).
    private object SettingsFiles(JsonElement a) =>
        LayoutFiles.Run(
            GetStringArgument(a, "action") == "snapshot",
            Required(a, "path"),
            snapshots ?? LayoutFiles.DefaultRoot,
            settings ?? SettingsLease.DefaultDirectory,
            () => (running ?? Te3Companion.RunningTe3)().Length > 0,
            GetArgument<bool?>(a, "whileRunning") ?? false
        );

    private static Window Live(Window? window) =>
        window ?? throw new InvalidOperationException("This action needs the live TE3 window.");

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

    private Task<object> ReopenPane(InputTally inputs, string handle, Window window, string title) =>
        LayoutSteps.Reopen(host, handle, window, inputs, title);

    internal static bool PaneVisible(Window window, string title) =>
        Parts(window).Any(part => part.Kind == "pane" && TitleMatches(part.Title, title));

    internal static bool DocumentOpen(Window window, string title) =>
        Parts(window).Any(part => part.Kind == "document" && TitleMatches(part.Title, title));

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

    private static object Relative(Rectangle r, Rectangle origin) =>
        new
        {
            x = r.X - origin.X,
            y = r.Y - origin.Y,
            width = r.Width,
            height = r.Height,
        };

    internal static bool CanCloseKind(string kind) => kind == "pane";

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

    private Task Click(InputTally inputs, string handle, AutomationElement element) =>
        Tool(inputs, "windows_click", new { @ref = host.Elements.Register(handle, element), handle });

    private Task Tool(InputTally inputs, string tool, object args) =>
        inputs.Invoke(() => host.Tools.ExecuteToolAsync(tool, JsonSerializer.SerializeToElement(args)), tool: tool);
}
