using FlaUI.Core.AutomationElements;
using FlaUI.Mcp.Core;

namespace FlaUI.Mcp.Tools;

/// <summary>
/// Where a keyboard tool (type, send keys, paste) sends: a ref's control, a window handle, or the foreground
/// window. One resolution for all three, so their checks before input cannot drift apart.
/// </summary>
internal sealed class KeyboardTargets(
    ElementRegistry elements,
    PendingInvokeTracker pending,
    ProcessPolicy policy,
    SessionManager? sessions
)
{
    /// <summary>The refusal for a target that cannot take keys, or null; nothing is focused or sent.</summary>
    public McpToolResult? Refusal(string? refId, string? handle)
    {
        if (handle != null && sessions == null)
        {
            return ToolBase.ErrorResult("Window-handle input requires a session manager.");
        }

        if (!string.IsNullOrWhiteSpace(refId))
        {
            elements.ResolveRef(refId, handle);
            // A blocked provider would hang the focus call. Input without a ref is pure keyboard and still works.
            return pending.TryGetPending(elements.GetProcessIdForRef(refId), out var blocked)
                ? ToolBase.BlockedResult(blocked)
                : null;
        }

        // Validate the explicit window; guarded input then focuses and verifies that exact window.
        if (handle != null && !policy.IsProcessAllowed(sessions!.GetInputTarget(handle).ProcessId))
        {
            return ToolBase.ErrorResult(RefusalCodes.ProcessNotAllowed, policy.DescribeDenied("Target process"));
        }

        return null;
    }

    /// <summary>The ref's control, or null without a ref.</summary>
    public AutomationElement? Element(string? refId) => refId == null ? null : elements.GetElement(refId);

    /// <summary>
    /// Guarded input on the target: takes the input lease, activates the window and focuses the ref's control. Focus
    /// is only verified under <paramref name="focus"/>; otherwise the guard checks the foreground window, which
    /// cannot tell a tree from a code editor in the same app.
    /// </summary>
    public GuardedInput Guard(string? refId, string? handle, AutomationElement? element, FocusRule focus) =>
        new(Input(refId, handle), element, focus);

    private InputTarget Input(string? refId, string? handle)
    {
        if (refId != null)
        {
            return elements.InputForRef(refId);
        }

        return handle != null ? sessions!.GetInputTarget(handle) : GuardedInput.ForegroundTarget(policy);
    }

    /// <summary>How a reply names the target.</summary>
    public static string Name(string? refId) => string.IsNullOrWhiteSpace(refId) ? "focused element" : refId;
}
