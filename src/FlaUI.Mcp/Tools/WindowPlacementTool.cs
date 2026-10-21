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
    public override object InputSchema =>
        new
        {
            type = "object",
            additionalProperties = false,
            properties = new
            {
                @ref = new { type = "string" },
                handle = new
                {
                    type = "string",
                    description = "Window identifier from windows_list_windows (for example w1), not a numeric HWND; alternative to ref.",
                },
                placement = new
                {
                    type = "object",
                    properties = new
                    {
                        x = new
                        {
                            type = "integer",
                            minimum = -100000,
                            maximum = 100000,
                        },
                        y = new
                        {
                            type = "integer",
                            minimum = -100000,
                            maximum = 100000,
                        },
                        width = new
                        {
                            type = "integer",
                            minimum = 100,
                            maximum = 8192,
                        },
                        height = new
                        {
                            type = "integer",
                            minimum = 100,
                            maximum = 8192,
                        },
                    },
                    required = new[] { "x", "y", "width", "height" },
                },
            },
            required = new[] { "placement" },
        };

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
                Outcome = new ToolOutcome("completed", "not-tracked") { Verification = "verified" },
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
        return ErrorResult($"Window did not land where requested: {mismatch}") with
        {
            Outcome = new ToolOutcome("completed", "not-tracked") { Verification = "mismatch" },
        };
    }
}
