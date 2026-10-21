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
        var target = SplitterPlan.RequireOne(GetArgument<int?>(a, "width"), GetArgument<int?>(a, "height"));
        var parts = Parts(window);
        var pane = FindPane(window, title);
        var before = pane.Container.BoundingRectangle;
        var others = parts.Where(p => p.Title != pane.Title).Select(p => p.Container.BoundingRectangle).Distinct();
        var documents = parts.Where(p => p.Kind == "document").Select(p => p.Container.BoundingRectangle);
        var width = GetArgument<int?>(a, "width");
        var current = width is null ? before.Height : before.Width;
        if (LayoutChecks.AlreadyThere(target, current))
        {
            return LayoutChecks.Skipped(title, target, current);
        }

        var (from, to) = SplitterPlan.Resize(
            before,
            others,
            width,
            GetArgument<int?>(a, "height"),
            window.BoundingRectangle,
            documents
        );
        var origin = window.BoundingRectangle;
        var start = new Point(from.X - origin.X, from.Y - origin.Y);
        var end = new Point(to.X - origin.X, to.Y - origin.Y);
        var sentBefore = inputs.Sent;
        await Drag(inputs, handle, title, start, end);
        var after = FindPane(window, title).Container.BoundingRectangle;
        var requested = width ?? GetArgument<int?>(a, "height")!.Value;
        var reached = width is null ? after.Height : after.Width;
        return LayoutChecks.Measured(
            new(
                title,
                SplitterPlan.Edge(before, from, width is not null),
                start,
                end,
                Relative(before, origin),
                Relative(after, origin),
                requested,
                reached,
                inputs.Sent - sentBefore
            )
        );
    }

    /// <summary>One drag in window pixels, then a check that it did not float the pane (see KNOWN-ISSUES).</summary>
    private async Task Drag(InputTally inputs, string handle, string title, Point from, Point to)
    {
        var pid = host.Sessions.GetWindowProcessId(handle);
        var windowsBefore = Win32Desktop.GetTopLevelWindows(pid).Select(w => w.Hwnd).ToList();
        await Tool(
            inputs,
            "windows_mouse",
            new
            {
                action = "drag",
                handle,
                from = new { x = from.X, y = from.Y },
                to = new { x = to.X, y = to.Y },
            }
        );
        await Task.Delay(SettleMs);
        SplitterPlan.ThrowIfUndocked(windowsBefore, Win32Desktop.GetTopLevelWindows(pid), title);
    }
}
