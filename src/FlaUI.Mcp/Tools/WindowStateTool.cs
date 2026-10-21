using System.Diagnostics;
using System.Text.Json;
using FlaUI.Mcp.Core;

namespace FlaUI.Mcp.Tools;

/// <summary>Maximize, restore or minimize an observed top-level window; reports the state it observed.</summary>
public sealed class WindowStateTool(ElementRegistry refs, PendingInvokeTracker pending, SessionManager? sessions = null)
    : ToolBase
{
    private static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SettlePoll = TimeSpan.FromMilliseconds(50);

    public override string Name => "windows_set_window_state";

    public override McpToolAnnotations Annotations => McpToolAnnotations.Input(destructive: false, idempotent: true);
    public override string Description =>
        "Maximize, restore (normal) or minimize a top-level window. Use restore before windows_place_window, which only moves normal-state windows. Returns the state and bounds observed afterwards; one attempt, never replayed.";
    public override object InputSchema =>
        new
        {
            type = "object",
            properties = new
            {
                handle = new
                {
                    type = "string",
                    description = "Window identifier from windows_list_windows (e.g. w1).",
                },
                @ref = new { type = "string", description = "Alternative to handle: a top-level window ref." },
                state = new { type = "string", @enum = new[] { "maximize", "restore", "normal", "minimize" } },
            },
            required = new[] { "state" },
        };

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var requested = WindowStates.Parse(GetStringArgument(arguments, "state"));
        var reference = GetStringArgument(arguments, "ref");
        var handle = GetStringArgument(arguments, "handle");
        if (reference == null && handle == null)
        {
            return Task.FromResult(ErrorResult("ref or handle required"));
        }

        if (handle != null && sessions == null)
        {
            return Task.FromResult(ErrorResult("Window-handle state changes require a session manager."));
        }

        if (reference != null && pending.TryGetPending(refs.GetProcessIdForRef(reference), out var blocked))
        {
            return Task.FromResult(BlockedResult(blocked));
        }

        OperationContext.Check();
        var target = reference == null ? sessions!.GetInputTarget(handle!) : refs.InputForRef(reference);
        var desktop = InputDesktop.Current;
        var before = desktop.WindowState(target.Hwnd);
        using (GuardedInput.AcquireLease())
        {
            // Windows drops window messages to a higher-integrity window, as it drops input.
            MutationGuard.Execute(
                () =>
                {
                    target.EnsureAlive();
                    GuardedInput.RequireDrivable(target.ProcessId, desktop);
                },
                () => desktop.SetWindowState(target.Hwnd, requested)
            );
        }

        var after = WaitForState(target.Hwnd, requested);
        var bounds = desktop.WindowBounds(target.Hwnd);
        var result = JsonSerializer.Serialize(
            new
            {
                requested,
                before,
                after,
                bounds = bounds is { } b
                    ? new
                    {
                        x = b.X,
                        y = b.Y,
                        width = b.Width,
                        height = b.Height,
                    }
                    : null,
            }
        );
        return Task.FromResult(
            after == requested
                ? TextResult(result)
                : ErrorResult($"State did not settle: {result}") with
                {
                    // The request went out in full; only its effect was not observed.
                    Outcome = new ToolOutcome("completed", "not-tracked") { Verification = "not-settled" },
                }
        );
    }

    private static string WaitForState(nint hwnd, string requested)
    {
        var clock = Stopwatch.StartNew();
        var observed = InputDesktop.Current.WindowState(hwnd);
        while (observed != requested && clock.Elapsed < SettleTimeout)
        {
            OperationContext.Check();
            Thread.Sleep(SettlePoll);
            observed = InputDesktop.Current.WindowState(hwnd);
        }

        return observed;
    }
}
