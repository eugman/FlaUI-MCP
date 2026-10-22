using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Mcp.Core;
using FlaUI.Mcp.Te3;

/// <summary>
/// te3_layout's pane actions on a layout surface: list, show, hide, close or keep, maximize and restore. Each call
/// that may open a dialog reads the TE3 windows once, before its first input, and checks every close against that.
/// </summary>
public sealed partial class Te3Layout
{
    // Docked panes and documents, then TE3's other titled top-level windows (a floating pane such as Dependencies is
    // one: unowned and not a tool window), which are outside the main window and so not among its parts.
    private static object List(ILayoutSurface surface)
    {
        var docked = surface.Parts().Select(p => new ListedPart(p.Title, p.Kind, Relative(p.Bounds, surface.Origin)));
        var floating = surface
            .Windows()
            .Where(w => w.Hwnd != surface.MainWindow && w.Title.Length > 0 && !w.IsToolWindow)
            .Select(w => new ListedPart(
                w.Title,
                LayoutChecks.IsFloatingPane(w, surface.ShapeOf) ? "floating" : "dialog",
                surface.BoundsOf(w.Hwnd) is { } bounds ? Relative(bounds, surface.Origin) : null
            ));
        return docked.Concat(floating).ToList();
    }

    // Floating panes only (title and AutomationId, LayoutChecks); a dialog is never listed as one.
    // Floating panes the call closes: named in close, or not kept in keep. Each goes through its window's own Close and
    // counts as closed only once the window is gone.
    private static async Task<List<object>> CloseFloating(ILayoutSurface surface, string[] titles, bool keep)
    {
        var closed = new List<object>();
        var targets = surface
            .Windows()
            .Where(w => w.Hwnd != surface.MainWindow && LayoutChecks.IsFloatingPane(w, surface.ShapeOf))
            .Where(w => titles.Any(t => TitleMatches(w.Title, t)) != keep)
            .ToList();
        foreach (var window in targets)
        {
            await surface.CloseWindow(window.Hwnd);
            for (var waited = 0; waited <= 1000 && surface.Windows().Any(w => w.Hwnd == window.Hwnd); waited += 200)
            {
                await surface.Delay(200);
            }

            if (surface.Windows().All(w => w.Hwnd != window.Hwnd))
            {
                closed.Add(window.Title);
            }
        }

        return closed;
    }

    private static List<string> FloatingTitles(ILayoutSurface surface) =>
        surface
            .Windows()
            .Where(w => w.Hwnd != surface.MainWindow && LayoutChecks.IsFloatingPane(w, surface.ShapeOf))
            .Select(w => w.Title)
            .ToList();

    private sealed record ListedPart(string Title, string Kind, object? Bounds);

    // A document's own tab Close (Invoke closes an unmodified document without a prompt, 3.27.1). Refused before input
    // for anything but an open document; read back as gone, and a dialog it opens (a save prompt) is reported.
    private static async Task<object> CloseDocument(ILayoutSurface surface, string title)
    {
        var baseline = LayoutChecks.Baseline(surface.Windows(), surface.MainWindow, surface.ShapeOf);
        var parts = surface.Parts();
        var document =
            parts.FirstOrDefault(p => p.Kind == "document" && TitleMatches(p.Title, title))
            ?? throw new ArgumentException(
                parts.Any(p => p.Kind == "pane" && TitleMatches(p.Title, title))
                    ? $"'{title}' is a docked pane, not a document; use hide-pane. No input sent."
                    : $"No open document titled '{title}'. Open documents: "
                        + string.Join(", ", parts.Where(p => p.Kind == "document").Select(p => $"'{p.Title}'"))
                        + ". No input sent."
            );
        await surface.Press(document.Title, "Close");
        await LayoutSteps.RequireNoDialog(surface, document.Title, baseline);
        if (surface.Parts().Any(p => p.Kind == "document" && TitleMatches(p.Title, document.Title)))
        {
            throw new InvalidOperationException(
                $"Document '{document.Title}' is still open after its Close; inspect before retrying."
            );
        }

        return new { closedDocument = document.Title };
    }

    private static object ShowPane(ILayoutSurface surface, string title)
    {
        if (!Te3PanePlan.IsNamedPane(title))
        {
            throw new ArgumentException("show-pane accepts AI Assistant, TOM Explorer or Properties.");
        }

        if (VisiblePane(surface, title) != null)
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

    private static LayoutPart? VisiblePane(ILayoutSurface surface, string title) =>
        surface.Parts().FirstOrDefault(part => part.Kind == "pane" && TitleMatches(part.Title, title));

    private static async Task<object> HidePane(ILayoutSurface surface, string title)
    {
        if (!Te3PanePlan.IsNamedPane(title))
        {
            throw new ArgumentException("hide-pane accepts AI Assistant, TOM Explorer or Properties.");
        }

        var baseline = LayoutChecks.Baseline(surface.Windows(), surface.MainWindow, surface.ShapeOf);
        if (VisiblePane(surface, title) is not { } pane)
        {
            return new
            {
                pane = title,
                visible = false,
                changed = false,
            };
        }

        await surface.Press(pane.Title, "Close");
        await LayoutSteps.RequireNoDialog(surface, pane.Title, baseline);
        if (VisiblePane(surface, title) != null)
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

    private static async Task<object> Close(ILayoutSurface surface, InputTally inputs, string[] titles, bool keep)
    {
        if (keep)
        {
            LayoutChecks.RequireKept(titles, surface.Parts().Select(p => p.Title).Concat(FloatingTitles(surface)));
        }

        // One snapshot for the whole call: a dialog that opens late after one close still stops the next.
        var baseline = LayoutChecks.Baseline(surface.Windows(), surface.MainWindow, surface.ShapeOf);
        var attempted = new HashSet<string>();
        var closes = 0;
        var closed = await LayoutSteps.Progress(
            "closed",
            async sent =>
            {
                var next =
                    closes++ < MaxCloses
                        ? surface
                            .Parts()
                            .FirstOrDefault(p => ShouldClose(p, titles, keep) && !attempted.Contains(p.Title))
                        : null;
                if (next == null)
                {
                    return null;
                }

                attempted.Add(next.Title);
                LayoutSteps.RequireNoNewWindow(surface, next.Title, baseline);
                await LayoutSteps.Sending(inputs, sent, next.Title, () => surface.Press(next.Title, "Close"));
                await LayoutSteps.RequireNoDialog(surface, next.Title, baseline);
                return next.Title;
            }
        );

        // A floating pane is a window of its own: closed through that window's Close, then read back.
        var floatingClosed = await CloseFloating(surface, titles, keep);
        closed.AddRange(floatingClosed);
        var remaining = surface.Parts().Where(p => p.Kind == "pane").Select(p => p.Title).ToList();
        var floating = FloatingTitles(surface);
        var unresolved = titles
            .Where(t =>
                remaining.Any(r => TitleMatches(r, t)) != keep || (!keep && floating.Any(f => TitleMatches(f, t)))
            )
            .ToList();
        return new
        {
            closed,
            remaining,
            unresolved,
            floating,
            floatingClosedWith = floatingClosed.Count > 0 ? "windows_close on the floating window's own handle" : null,
        };
    }

    private static bool ShouldClose(LayoutPart part, string[] titles, bool keep) =>
        CanCloseKind(part.Kind) && titles.Any(t => TitleMatches(part.Title, t)) != keep;

    // A docked pane shows Maximize; once maximized the same button is named Restore (observed on 3.27.1).
    private static async Task<object> PaneButton(ILayoutSurface surface, string title, string button)
    {
        var pane =
            VisiblePane(surface, title)
            ?? throw new ArgumentException($"No docked pane titled '{title}'; use action list.");
        if (!surface.ButtonShown(pane.Title, button))
        {
            throw new InvalidOperationException(
                button == "Maximize"
                    ? $"Pane '{pane.Title}' has no Maximize button (a tab-group pane may not, or it is already maximized)."
                    : $"Pane '{pane.Title}' has no Restore button; it is not maximized."
            );
        }

        await surface.Press(pane.Title, button);
        // Maximize and Restore are one button renamed, so the other name showing is the read-back.
        var other = button == "Maximize" ? "Restore" : "Maximize";
        await LayoutChecks.ConfirmPaneButton(
            pane.Title,
            button,
            () => surface.ButtonShown(pane.Title, other),
            surface.Delay
        );
        return button == "Maximize"
            ? new { maximized = pane.Title, readBack = other }
            : (object)new { restored = pane.Title, readBack = other };
    }

    // Core's windows_close on the floating window's own handle: the input lease, identity and grant apply.
    private Task CloseFloatingWindow(InputTally inputs, nint hwnd, int processId) =>
        Tool(inputs, "windows_close", new { handle = host.Sessions.RegisterNativeWindow(hwnd, processId) });

    /// <summary>The live TE3 window: its parts through UIA, and presses through Core's guarded click.</summary>
    private sealed class UiaSurface(Te3Layout tool, InputTally inputs, string handle, Window window) : ILayoutSurface
    {
        public System.Drawing.Rectangle Origin => window.BoundingRectangle;

        public nint MainWindow => window.Properties.NativeWindowHandle.ValueOrDefault;

        public IReadOnlyList<LayoutPart> Parts() =>
            [.. Te3Layout.Parts(window).Select(p => new LayoutPart(p.Title, p.Kind, p.Container.BoundingRectangle))];

        public bool ButtonShown(string title, string button) => Button(title, button) != null;

        public Task Press(string title, string button) =>
            tool.Click(
                inputs,
                handle,
                Button(title, button)
                    ?? throw new InvalidOperationException($"'{title}' shows no {button} button; nothing was pressed.")
            );

        public System.Drawing.Rectangle? BoundsOf(nint hwnd) => Win32Desktop.GetWindowBounds(hwnd);

        public WindowShape? ShapeOf(nint hwnd) => WindowShape.Read(hwnd);

        public IReadOnlyList<Win32WindowInfo> Windows() =>
            Win32Desktop.GetTopLevelWindows(window.Properties.ProcessId.ValueOrDefault);

        public Task Delay(int ms) => Task.Delay(ms);

        public Task CloseWindow(nint hwnd) =>
            tool.CloseFloatingWindow(inputs, hwnd, window.Properties.ProcessId.ValueOrDefault);

        // Close is the part's own; Maximize and Restore sit beside it on the pane's title bar.
        private AutomationElement? Button(string title, string button)
        {
            var part = Te3Layout.Parts(window).FirstOrDefault(p => TitleMatches(p.Title, title));
            return button == "Close"
                ? part?.Close
                : part?.Close.Parent?.FindFirstChild(cf => cf.ByControlType(ControlType.Button).And(cf.ByName(button)));
        }
    }
}
