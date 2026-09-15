using System.Text.Json;
using FlaUI.Core.WindowsAPI;
using PlaywrightWindows.Mcp.Core;

namespace PlaywrightWindows.Mcp.Tools;

/// <summary>
/// Paste text through the clipboard. Typed keystrokes go through editor autocomplete; a paste does not.
/// </summary>
public sealed class PasteTool(ElementRegistry elements, PendingInvokeTracker pending, ProcessPolicy policy, SessionManager? sessions) : ToolBase
{
    public override string Name => "windows_paste";

    public override string Description =>
        "Paste text into an element by ref, or the focused element, with the clipboard and Ctrl+V. " +
        "Use it for code and multi-line text, which code editors autocomplete when typed. " +
        "Overwrites the clipboard; the app may still reformat what it receives.";

    public override object InputSchema => new
    {
        type = "object",
        properties = new
        {
            handle = new { type = "string", description = "Optional explicit window handle; otherwise uses the focused element." },
            @ref = new { type = "string", description = "Element ref from windows_snapshot or windows_find. If omitted, pastes into the focused element." },
            text = new { type = "string", description = "Text to paste" }
        },
        required = new[] { "text" }
    };

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var text = GetStringArgument(arguments, "text");
        if (text == null) return Task.FromResult(ErrorResult("Missing required argument: text"));
        var refId = GetStringArgument(arguments, "ref");
        try
        {
            OperationContext.Check();
            var handle = GetStringArgument(arguments, "handle");
            if (handle != null && sessions == null)
                return Task.FromResult(ErrorResult("Window-handle input requires a session manager."));
            if (!string.IsNullOrEmpty(refId))
            {
                elements.ResolveRef(refId, handle);
                if (pending.TryGetPending(elements.GetProcessIdForRef(refId), out var blocked))
                    return Task.FromResult(BlockedResult(blocked));
            }
            else if (handle != null && !policy.IsProcessAllowed(sessions!.GetInputTarget(handle).ProcessId))
            {
                return Task.FromResult(ErrorResult(policy.DescribeDenied("Target process")));
            }

            // Take the input lease and verify focus before touching the clipboard.
            using var input = new GuardedInput(refId != null ? elements.InputForRef(refId) : handle != null ? sessions!.GetInputTarget(handle) : GuardedInput.ForegroundTarget(policy),
                refId == null ? null : elements.GetElement(refId));
            // Clipboard text uses CRLF; edit controls don't break lines on a bare LF.
            if (!Win32Desktop.SetClipboardText(text.ReplaceLineEndings("\r\n")))
                return Task.FromResult(ErrorResult("The clipboard is in use by another process; no input sent."));
            input.Send(() => SendKeysTool.PressKeys([VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_V]));
            return Task.FromResult(TextResult($"Pasted {text.Length} characters into {(string.IsNullOrEmpty(refId) ? "focused element" : refId)}. The clipboard now holds that text."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ErrorResult($"Failed to paste: {ex.Message}"));
        }
    }
}
