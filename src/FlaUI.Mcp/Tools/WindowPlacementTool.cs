using System.Text.Json;
using FlaUI.Core.Definitions;
using FlaUI.Mcp.Core;

namespace FlaUI.Mcp.Tools;

/// <summary>Move/resize an observed native window, never a child control or inferred window.</summary>
public sealed class WindowPlacementTool(
    ElementRegistry refs,
    PendingInvokeTracker pending,
    SessionManager? sessions = null
) : ToolBase
{
    public override string Name => "windows_place_window";

    public override McpToolAnnotations Annotations => McpToolAnnotations.Input(destructive: false, idempotent: true);
    public override string Description =>
        "Move or resize an observed normal-state native window in physical screen pixels without activating it. Returns requested and observed bounds; the operating system may constrain size. Rejects clipping and child controls; never replays the move.";
    public override object InputSchema => ToolSchema.For<Arguments>();

    /// <summary>The tool's arguments; its schema is generated from them.</summary>
    internal sealed record Arguments
    {
        [ToolDoc("A top-level window ref; alternative to handle.")]
        public string? Ref { get; init; }

        [ToolDoc(
            "Window identifier from windows_list_windows (for example w1), not a numeric HWND; alternative to ref."
        )]
        public string? Handle { get; init; }

        [ToolDoc("The bounds to request, in physical screen pixels.")]
        public required PlacementArgument Placement { get; init; }
    }

    /// <summary>One argument, an object.</summary>
    internal sealed record PlacementArgument
    {
        [ToolRange(-100000, Maximum = 100000)]
        public required int X { get; init; }

        [ToolRange(-100000, Maximum = 100000)]
        public required int Y { get; init; }

        [ToolRange(100, Maximum = 8192)]
        public required int Width { get; init; }

        [ToolRange(100, Maximum = 8192)]
        public required int Height { get; init; }
    }

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var a = arguments ?? throw new ArgumentException("Arguments required");
        var reference = GetStringArgument(arguments, "ref");
        var handle = GetStringArgument(arguments, "handle");
        if (reference == null && handle == null)
        {
            return Task.FromResult(ErrorResult("ref or handle required"));
        }

        if (handle != null && sessions == null)
        {
            return Task.FromResult(ErrorResult("Window-handle placement requires a session manager."));
        }

        if (reference != null && handle != null)
        {
            refs.ValidateReference(reference, handle);
        }

        var p = a.GetProperty("placement");
        // Explicit GetProperty calls also reject missing coordinates; record defaults must not imply (0,0).
        var placement = new WindowPlacement(
            p.GetProperty("x").GetInt32(),
            p.GetProperty("y").GetInt32(),
            p.GetProperty("width").GetInt32(),
            p.GetProperty("height").GetInt32()
        );
        placement.Validate();
        var pid = reference == null ? 0 : refs.GetProcessIdForRef(reference);
        if (reference != null && pending.TryGetPending(pid, out var blocked))
        {
            return Task.FromResult(BlockedResult(blocked));
        }

        OperationContext.Check();
        var target = reference == null ? sessions!.GetInputTarget(handle!) : refs.InputForRef(reference);
        if (reference != null)
        {
            var element =
                refs.GetElement(reference) ?? throw new ArgumentException("Unknown ref; refresh scoped discovery");
            refs.ValidateReference(reference);
            var native = element.Properties.NativeWindowHandle.ValueOrDefault;
            if (
                element.Properties.ControlType.ValueOrDefault != ControlType.Window
                || native == 0
                || native != target.Hwnd
            )
            {
                throw new ArgumentException(
                    "Placement requires an exact native top-level window ref, not a child or popup menu"
                );
            }
        }

        var desktop = InputDesktop.Current;
        using var lease = GuardedInput.AcquireLease();
        void Validate()
        {
            OperationContext.Check();
            if (reference != null)
            {
                refs.ValidateReference(reference);
            }

            target.EnsureAlive();
            // Windows drops window messages to a higher-integrity window, as it drops input.
            GuardedInput.RequireDrivable(target.ProcessId, desktop);
            if (!desktop.IsNormalWindow(target.Hwnd))
            {
                throw new InvalidOperationException(
                    "Placement requires a visible, normal-state window; restore/maximize is a separate action"
                );
            }

            placement.RequireVisible(System.Windows.Forms.Screen.AllScreens.Select(s => s.WorkingArea));
        }

        var before =
            desktop.WindowBounds(target.Hwnd) ?? throw new InvalidOperationException("Window bounds unavailable");
        MutationGuard.Execute(Validate, () => desktop.PlaceWindow(target.Hwnd, placement.Bounds));
        target.EnsureAlive();
        var after =
            desktop.WindowBounds(target.Hwnd)
            ?? throw new InvalidOperationException("Window disappeared after placement; mutation not replayed");
        return Task.FromResult(PlacementResult(placement.Bounds, before, after));
    }

    /// <summary>
    /// Verified when the window landed exactly where requested. Otherwise (DPI, a minimum size or the shell
    /// constrained it) a failure with the deltas; the move went out in full and is never replayed.
    /// </summary>
    /// <summary>The window rectangle after the move, read back through Win32, against the request.</summary>
    private static Proof PlacementProof(System.Drawing.Rectangle requested, System.Drawing.Rectangle after)
    {
        static string Box(System.Drawing.Rectangle r) => $"{r.X},{r.Y} {r.Width}x{r.Height}";
        return Proof.Compare(Box(requested), Box(after), "window-rect", independent: true);
    }

    internal static McpToolResult PlacementResult(
        System.Drawing.Rectangle requested,
        System.Drawing.Rectangle before,
        System.Drawing.Rectangle after
    )
    {
        static object Box(System.Drawing.Rectangle r) =>
            new
            {
                x = r.X,
                y = r.Y,
                width = r.Width,
                height = r.Height,
            };
        var report = new
        {
            requested = Box(requested),
            before = Box(before),
            after = Box(after),
        };
        if (after == requested)
        {
            return TextResult(JsonSerializer.Serialize(report)) with
            {
                Outcome = new ToolOutcome("completed", "not-tracked").WithProof(PlacementProof(requested, after)),
            };
        }

        var delta = new
        {
            x = after.X - requested.X,
            y = after.Y - requested.Y,
            width = after.Width - requested.Width,
            height = after.Height - requested.Height,
        };
        var mismatch = JsonSerializer.Serialize(
            new
            {
                report.requested,
                report.before,
                report.after,
                delta,
            }
        );
        var text = $"Window did not land where requested: {mismatch}";
        return ErrorResult(text) with
        {
            // Its own code, so a host can tell a constrained placement from a refusal or a failed call.
            Outcome = new ToolOutcome("completed", "not-tracked")
            {
                Failure = new InputRefusal(RefusalCodes.PlacementMismatch, text) { ActionDispatched = true },
            }.WithProof(PlacementProof(requested, after)),
        };
    }
}
