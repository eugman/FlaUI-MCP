using System.Text.Json;
using FlaUI.Mcp.Core;

namespace FlaUI.Mcp.Tools;

/// <summary>
/// List all open windows
/// </summary>
public class ListWindowsTool : ToolBase
{
    private readonly SessionManager _sessionManager;
    private readonly ProcessPolicy _policy;

    public ListWindowsTool(SessionManager sessionManager, ProcessPolicy? policy = null)
    {
        _sessionManager = sessionManager;
        _policy = policy ?? ProcessPolicy.AllowAll;
    }

    // Under an allowlist an empty list usually means the allowed app isn't running, not a broken server.
    internal static string EmptyMessage(ProcessPolicy policy) =>
        policy.IsRestricted ? $"No windows found ({policy.DescribeAllowed()})" : "No windows found";

    public override string Name => "windows_list_windows";

    protected override bool Observation => true;

    protected override bool Recovery => true;

    public override McpToolAnnotations Annotations => McpToolAnnotations.ReadOnly;

    public override string Description =>
        "List all open windows with their handles, titles, and process names. "
        + "Use this to find windows to interact with.";

    public override object InputSchema => ToolSchema.For<NoArguments>();

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        try
        {
            var windows = _sessionManager.ListWindows();
            // Right after a launch or a closed dialog, windows can briefly be untitled; re-check before reporting none.
            for (var waited = 0; windows.Count == 0 && waited < 1000; waited += 250)
            {
                Thread.Sleep(250);
                windows = _sessionManager.ListWindows();
            }

            if (windows.Count == 0)
            {
                return Task.FromResult(TextResult(EmptyMessage(_policy)));
            }

            var lines = windows.Select(w => $"- {w.handle}: \"{w.title}\" ({w.processName ?? "unknown"})");

            return Task.FromResult(TextResult(string.Join("\n", lines)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ErrorResult($"Failed to list windows: {ex.Message}"));
        }
    }
}

/// <summary>
/// Focus a window
/// </summary>
public class FocusWindowTool : ToolBase
{
    private readonly SessionManager _sessionManager;
    private readonly PendingInvokeTracker? _pending;

    public FocusWindowTool(SessionManager sessionManager, PendingInvokeTracker? pending = null)
    {
        _sessionManager = sessionManager;
        _pending = pending;
    }

    public override string Name => "windows_focus";

    protected override bool Recovery => true;

    public override McpToolAnnotations Annotations => McpToolAnnotations.Input(destructive: false, idempotent: true);

    public override string Description => "Bring a window to the foreground and give it focus.";

    public override object InputSchema => ToolSchema.For<Arguments>();

    /// <summary>The tool's arguments; its schema is generated from them.</summary>
    internal sealed record Arguments
    {
        [ToolDoc("Window handle from windows_list_windows or windows_launch")]
        public string? Handle { get; init; }

        [ToolDoc("Exact window title (alternative to handle).")]
        public string? Title { get; init; }
    }

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var handle = GetStringArgument(arguments, "handle");
        var title = GetStringArgument(arguments, "title");

        try
        {
            if (!string.IsNullOrEmpty(handle))
            {
                var uia = Focus(handle);
                return Task.FromResult(
                    StillPending(uia, $"Focus on {handle}")
                        ?? FocusResult(handle, _sessionManager.GetWindowHwnd(handle))
                );
            }
            else if (!string.IsNullOrEmpty(title))
            {
                var (windowHandle, window) = _sessionManager.AttachToWindow(title);
                var uia = Focus(windowHandle);
                return Task.FromResult(
                    StillPending(uia, $"Focus on {windowHandle}")
                        ?? FocusResult(windowHandle, _sessionManager.GetWindowHwnd(windowHandle))
                );
            }
            else
            {
                return Task.FromResult(ErrorResult("Either 'handle' or 'title' is required"));
            }
        }
        catch (ProviderBlockedException blocked)
        {
            return Task.FromResult(BlockedResult(blocked.Pending));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ErrorResult($"Failed to focus window: {ex.Message}"));
        }
    }

    /// <summary>Focus by Win32 (null result), or by UIA for a window without a native handle.</summary>
    private PatternCallResult? Focus(string handle)
    {
        if (_sessionManager.UiaWindowFor(handle, _pending) is not { } window)
        {
            _sessionManager.FocusWindow(handle, _pending);
            return null;
        }

        return WindowMutation.Uia(window.Properties.ProcessId.Value, $"Focus on {handle}", window.Focus, _pending);
    }

    /// <summary>
    /// Ok only when the target window (or its own popup) is in front afterwards. A window known only through UI
    /// Automation cannot be observed, so its reply says so and stays unverified.
    /// </summary>
    private static McpToolResult FocusResult(string handle, nint hwnd)
    {
        const string Unobserved =
            "foreground activation was not observed. Do not send input until the target is active.";
        if (hwnd == 0)
        {
            return TextResult($"Focus requested for {handle}, but {Unobserved}") with
            {
                Outcome = new ToolOutcome("completed", "not-tracked").WithProof(
                    new Proof(Proof.Unavailable, null, handle, "foreground", Independent: true)
                ),
            };
        }

        var desktop = InputDesktop.Current;
        var foreground = desktop.Foreground();
        if (desktop.BelongsTo(foreground, hwnd))
        {
            return TextResult($"Focused window {handle}") with
            {
                Outcome = new ToolOutcome("completed", "not-tracked").WithProof(
                    new Proof(Proof.Verified, $"{foreground}", $"{hwnd}", "foreground", Independent: true)
                ),
            };
        }

        var pid = desktop.ProcessOf(foreground);
        var owner = $"HWND {foreground}";
        if (pid != 0)
        {
            var name = desktop.ProcessNameOf(pid);
            owner = $"PID {pid}";
            if (name != null)
            {
                owner += $" ({name})";
            }
        }

        return ErrorResult($"Focus requested for {handle}, but another window is in front ({owner}): {Unobserved}");
    }
}

/// <summary>
/// Close a window
/// </summary>
public class CloseWindowTool : ToolBase
{
    private readonly SessionManager _sessionManager;
    private readonly PendingInvokeTracker? _pending;

    public CloseWindowTool(SessionManager sessionManager, PendingInvokeTracker? pending = null)
    {
        _sessionManager = sessionManager;
        _pending = pending;
    }

    public override string Name => "windows_close";

    protected override bool Recovery => true;

    public override McpToolAnnotations Annotations => McpToolAnnotations.Input(destructive: true);

    public override string Description => "Close a window.";

    public override object InputSchema => ToolSchema.For<Arguments>();

    /// <summary>The tool's arguments; its schema is generated from them.</summary>
    internal sealed record Arguments
    {
        [ToolDoc("Window handle to close")]
        public required string Handle { get; init; }
    }

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var handle = GetStringArgument(arguments, "handle");
        if (string.IsNullOrEmpty(handle))
        {
            return Task.FromResult(ErrorResult("Missing required argument: handle"));
        }

        try
        {
            PatternCallResult? uia = null;
            if (_sessionManager.UiaWindowFor(handle, _pending) is { } window)
            {
                // UIA Close is also a request, not proof of disappearance.
                uia = WindowMutation.Uia(
                    window.Properties.ProcessId.Value,
                    $"Close on {handle}",
                    window.Close,
                    _pending
                );
            }
            else
            {
                _sessionManager.CloseWindow(handle, _pending);
            }

            return Task.FromResult(
                StillPending(uia, $"Close on {handle}")
                    ?? TextResult(
                        $"Close requested for window {handle}. A save prompt may keep it open; use windows_list_windows to verify before continuing."
                    )
            );
        }
        catch (ProviderBlockedException blocked)
        {
            return Task.FromResult(BlockedResult(blocked.Pending));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ErrorResult($"Failed to close window: {ex.Message}"));
        }
    }
}
