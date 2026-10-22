using System.Drawing;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Mcp.Core;
using FlaUI.Mcp.Te3;

/// <summary>te3_layout resize and apply-preset: one splitter drag per pane, measured after each.</summary>
public sealed partial class Te3Layout
{
    private const int SettleMs = 300;

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
        try
        {
            // Stops at the first step that misses: each step is one drag, so a miss costs one drag's input.
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
                        async () => changed = await Resize(inputs, handle, window, size.Pane, args)
                    );
                    return changed;
                }
            );
            return new { preset, changes };
        }
        catch (LayoutSteps.ProgressException failure)
        {
            throw LayoutSteps.ReadBack(failure, () => List(new UiaSurface(this, inputs, handle, window)));
        }
    }

    // A drag that leaves the pane outside the tolerance fails the call, for resize as for each preset step.
    private async Task<object> Resize(InputTally inputs, string handle, Window window, string title, JsonElement a)
    {
        var width = GetArgument<int?>(a, "width");
        var height = GetArgument<int?>(a, "height");
        var target = SplitterPlan.RequireOne(width, height);
        var parts = Parts(window);
        var pane =
            parts.FirstOrDefault(p => p.Kind == "pane" && TitleMatches(p.Title, title))
            ?? throw new ArgumentException($"No docked pane titled '{title}'; use action list.");
        var seen = Snapshot(window, parts, pane);
        var before = seen.Pane;
        var current = width is null ? before.Height : before.Width;
        if (LayoutChecks.AlreadyThere(target, current))
        {
            return LayoutChecks.Skipped(title, target, current);
        }

        var (from, to) = SplitterPlan.Resize(
            before,
            seen.Others,
            width,
            height,
            seen.Origin,
            seen.Documents,
            DpiUtility.WindowDpi(host.Sessions.GetInputTarget(handle).Hwnd) ?? SplitterPlan.BaseDpi
        );
        var origin = seen.Origin;
        var start = new Point(from.X - origin.X, from.Y - origin.Y);
        var end = new Point(to.X - origin.X, to.Y - origin.Y);
        var sentBefore = inputs.Sent;
        await Drag(inputs, handle, title, start, end, Captions(parts));
        var after = FindPane(window, title).Container.BoundingRectangle;
        var reached = width is null ? after.Height : after.Width;
        return LayoutChecks.Measured(
            new(
                title,
                SplitterPlan.Edge(before, from, width is not null),
                start,
                end,
                Relative(before, origin),
                Relative(after, origin),
                target,
                reached,
                inputs.Sent - sentBefore
            )
        );
    }

    /// <summary>Every rectangle a resize drag is planned from, read in one pass (see <see cref="Snapshot"/>).</summary>
    private sealed record PaneSnapshot(
        Rectangle Origin,
        Rectangle Pane,
        List<Rectangle> Others,
        List<Rectangle> Documents
    );

    // The window's origin and the parts' bounds in one pass, the origin read again after them: a window that moved
    // in between would shift the raw-point drag, so the snapshot is refused (LayoutChecks.RequireStill).
    private static PaneSnapshot Snapshot(Window window, List<Part> parts, Part pane)
    {
        var origin = window.BoundingRectangle;
        var bounds = parts.Select(p => (p.Title, p.Kind, Rect: p.Container.BoundingRectangle)).ToList();
        var own = pane.Container.BoundingRectangle;
        LayoutChecks.RequireStill(origin, window.BoundingRectangle);
        return new(
            origin,
            own,
            [.. bounds.Where(b => b.Title != pane.Title).Select(b => b.Rect).Distinct()],
            [.. bounds.Where(b => b.Kind == "document").Select(b => b.Rect)]
        );
    }

    // Every caption and document tab in view, the resized pane's own included (SplitterPlan.Avoided).
    private static List<AutomationElement> Captions(List<Part> parts) =>
        SplitterPlan.Avoided(parts.Select(p => (p.Title, p.Close.Parent)));

    /// <summary>
    /// One drag in window pixels, refused by Core before the button goes down when the start lands on one of
    /// <paramref name="captions"/> (fromMustNotHit), then a check that it did not float the pane (see KNOWN-ISSUES).
    /// </summary>
    private async Task Drag(
        InputTally inputs,
        string handle,
        string title,
        Point from,
        Point to,
        List<AutomationElement> captions
    )
    {
        var pid = host.Sessions.GetWindowProcessId(handle);
        var avoid = captions.Select(caption => host.Elements.Register(handle, caption)).ToArray();
        var windowsBefore = Win32Desktop.GetTopLevelWindows(pid).Select(w => w.Hwnd).ToList();
        await Tool(inputs, "windows_mouse", SplitterPlan.DragArguments(handle, from, to, avoid));
        await Task.Delay(SettleMs);
        SplitterPlan.ThrowIfUndocked(windowsBefore, Win32Desktop.GetTopLevelWindows(pid), title);
    }
}
