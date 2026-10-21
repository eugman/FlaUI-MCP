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
    private static object List(ILayoutSurface surface) =>
        surface
            .Parts()
            .Select(p => new
            {
                p.Title,
                p.Kind,
                bounds = Relative(p.Bounds, surface.Origin),
            })
            .ToList();

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

        var baseline = LayoutChecks.Baseline(surface.Windows(), surface.MainWindow);
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
            LayoutChecks.RequireKept(titles, surface.Parts().Select(p => p.Title));
        }

        // One snapshot for the whole call: a dialog that opens late after one close still stops the next.
        var baseline = LayoutChecks.Baseline(surface.Windows(), surface.MainWindow);
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

        var remaining = surface.Parts().Where(p => p.Kind == "pane").Select(p => p.Title).ToList();
        var unresolved = titles.Where(t => remaining.Any(r => TitleMatches(r, t)) != keep).ToList();
        return new
        {
            closed,
            remaining,
            unresolved,
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

        public IReadOnlyList<Win32WindowInfo> Windows() =>
            Win32Desktop.GetTopLevelWindows(window.Properties.ProcessId.ValueOrDefault);

        public Task Delay(int ms) => Task.Delay(ms);

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
