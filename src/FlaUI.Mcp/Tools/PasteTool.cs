using System.Text.Json;
using FlaUI.Core.WindowsAPI;
using FlaUI.Mcp.Core;

namespace FlaUI.Mcp.Tools;

/// <summary>
/// Paste text through the clipboard. Pasting can avoid per-keystroke autocomplete in code editors,
/// though the target application may still transform pasted text.
/// </summary>
public sealed class PasteTool(
    ElementRegistry elements,
    PendingInvokeTracker pending,
    ProcessPolicy policy,
    SessionManager? sessions
) : ToolBase
{
    public override string Name => "windows_paste";

    public override McpToolAnnotations Annotations => McpToolAnnotations.Input(destructive: true);

    public override string Description =>
        "Paste text into an element by ref, or the focused element, with the clipboard and Ctrl+V. "
        + "Use it for code and multi-line text, which code editors autocomplete when typed. "
        + "Overwrites the clipboard; the app may still reformat what it receives.";

    public override object InputSchema => ToolSchema.For<Arguments>();

    /// <summary>The tool's arguments; its schema is generated from them.</summary>
    internal sealed record Arguments
    {
        [ToolDoc("Optional explicit window handle; otherwise uses the focused element.")]
        public string? Handle { get; init; }

        [ToolDoc("Element ref from windows_snapshot or windows_find. If omitted, pastes into the focused element.")]
        public string? Ref { get; init; }

        [ToolDoc(
            "Require ref and verified keyboard focus on that exact control before pasting (default false). Worth setting for code editors: an unverified paste inserts text wherever focus happens to be."
        )]
        public bool? VerifyFocus { get; init; }

        [ToolDoc("Text to paste")]
        public required string Text { get; init; }
    }

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var text = GetStringArgument(arguments, "text");
        if (text == null)
        {
            return Task.FromResult(ErrorResult(RefusalCodes.InvalidArguments, "Missing required argument: text"));
        }

        var refId = GetStringArgument(arguments, "ref");
        try
        {
            OperationContext.Check();
            var handle = GetStringArgument(arguments, "handle");
            if (handle != null && sessions == null)
            {
                return Task.FromResult(ErrorResult("Window-handle input requires a session manager."));
            }

            if (!string.IsNullOrEmpty(refId))
            {
                elements.ResolveRef(refId, handle);
                if (pending.TryGetPending(elements.GetProcessIdForRef(refId), out var blocked))
                {
                    return Task.FromResult(BlockedResult(blocked));
                }
            }
            else if (handle != null && !policy.IsProcessAllowed(sessions!.GetInputTarget(handle).ProcessId))
            {
                return Task.FromResult(ErrorResult(policy.DescribeDenied("Target process")));
            }

            var verifyFocus = GetBoolArgument(arguments, "verifyFocus");
            if (verifyFocus && string.IsNullOrEmpty(refId))
            {
                return Task.FromResult(
                    ErrorResult(
                        RefusalCodes.InvalidArguments,
                        "verifyFocus requires an element ref; nothing was pasted."
                    )
                );
            }

            var element = refId == null ? null : elements.GetElement(refId);
            // Takes the input lease and activates the target. Focus is only *verified* when the
            // caller asks: GuardedInput otherwise checks the foreground process, which cannot tell a
            // tree from a code editor in the same app.
            using var input = new GuardedInput(
                refId != null ? elements.InputForRef(refId)
                    : handle != null ? sessions!.GetInputTarget(handle)
                    : GuardedInput.ForegroundTarget(policy),
                element,
                verifyFocus
            );
            var target = string.IsNullOrEmpty(refId) ? "focused element" : refId;
            return Task.FromResult(Paste(input, element, text, target));
        }
        catch (ProviderBlockedException blocked)
        {
            // Keep the structured blocked result rather than flattening it into a message.
            return Task.FromResult(BlockedResult(blocked.Pending));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ErrorResult($"Failed to paste: {ex.Message}"));
        }
    }

    /// <summary>Put the text on the clipboard, send Ctrl+V through guarded input, and report what arrived.</summary>
    internal McpToolResult Paste(
        GuardedInput input,
        FlaUI.Core.AutomationElements.AutomationElement? element,
        string text,
        string target
    )
    {
        // Clipboard text uses CRLF; edit controls don't break lines on a bare LF.
        var clipboardText = text.ReplaceLineEndings("\r\n");
        // Replacing the user's clipboard is a change of its own, made before Ctrl+V can be refused, so it is
        // checked again (permission, foreground) right before the write and counted once written.
        var written = false;
        input.Check(() => written = input.SetClipboardText(clipboardText));
        if (!written)
        {
            const string Busy = "The clipboard is in use by another process, so nothing was pasted.";
            return ErrorResult(
                input.Changed is { } changed
                    ? $"{Busy} Before this, {changed}, so the desktop foreground changed."
                    : Busy
            );
        }

        try
        {
            return SendPaste(input, element, clipboardText, target);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ProviderBlockedException)
        {
            return ErrorResult($"Failed to paste: {ex.Message} The clipboard now holds that text.");
        }
    }

    /// <summary>Send Ctrl+V through guarded input and report what arrived.</summary>
    private McpToolResult SendPaste(
        GuardedInput input,
        FlaUI.Core.AutomationElements.AutomationElement? element,
        string clipboardText,
        string target
    )
    {
        // Read before pasting: "the control contains the text" is not evidence on its own,
        // because a field that already contained it reads the same either way.
        var before = ReadValue(element);
        // Focus sampled before Ctrl+V; it may change before the app processes the paste.
        var preSendFocus = InputDesktop.Current.FocusedControl();
        input.Send(() => SendKeysTool.PressKeys([VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_V]));

        // Ctrl+V is already dispatched. Nothing below may report a failure to paste: a
        // read-back problem is missing evidence, not an unsent keystroke.
        string note;
        PasteVerdict verdict;
        try
        {
            verdict = PasteEvidence(element, clipboardText, before);
            note =
                SendKeysTool.DescribeFocus(preSendFocus, policy, "focus before send")
                + ". The clipboard now holds that text."
                + verdict.Sentence;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            verdict = new("unavailable", $" Not verified: reading back after the paste failed ({ex.Message}).");
            note = ". The clipboard now holds that text." + verdict.Sentence;
        }

        return PasteReply(
            $"Sent Ctrl+V for {clipboardText.Length} characters to {target}{note}",
            verdict,
            verdict.Observed,
            clipboardText
        );
    }

    /// <summary>
    /// The reply with its verdict. A read-back that shows the paste did not apply (unchanged without the text,
    /// or changed without it) is a failure; Ctrl+V went out, so the dispatch stays completed. An unchanged value
    /// that already holds the text (identical text re-pasted) is not.
    /// </summary>
    internal static McpToolResult PasteReply(
        string text,
        PasteVerdict verdict,
        string? observed = null,
        string? expected = null
    ) =>
        (verdict.Verification is "not-applied" or "mismatch" ? ErrorResult(text) : TextResult(text)) with
        {
            // The Value read back after Ctrl+V is independent of the keystroke that pasted. An identical re-paste
            // (unchanged) is a no-op for the proof, so its status is ok.
            Outcome = new ToolOutcome("completed", "not-tracked")
            {
                Verification = verdict.Verification,
                Proof = new Proof(
                    verdict.Verification == "unchanged" ? Proof.NoOp : verdict.Verification,
                    observed,
                    expected,
                    "Value",
                    Independent: true
                ),
            },
        };

    /// <summary>
    /// What a paste's read-back establishes, as the outcome's verification (verified, unchanged, mismatch,
    /// unavailable or not-established) and the sentence the reply gives.
    /// </summary>
    internal sealed record PasteVerdict(string Verification, string Sentence)
    {
        /// <summary>The value read back, when one was.</summary>
        public string? Observed { get; init; }
    }

    /// <summary>
    /// Read the target back where it exposes a value, so the reply reports what arrived rather than
    /// only that Ctrl+V was sent. A read-only editor, or an app that binds Ctrl+V to something else,
    /// otherwise produces an identical success message.
    /// </summary>
    /// <param name="element">The pasted-into element, or null for a ref-less paste.</param>
    /// <param name="expected">The text placed on the clipboard.</param>
    /// <returns>The verdict, with a sentence confirming or questioning the result.</returns>
    internal static PasteVerdict PasteEvidence(
        FlaUI.Core.AutomationElements.AutomationElement? element,
        string expected,
        string? before,
        int settleMilliseconds = SettleMilliseconds
    )
    {
        if (element == null)
        {
            return new("not-established", " Not verified: no ref was given, so there is nothing to read back.");
        }

        bool supported;
        try
        {
            supported = element.Patterns.Value.IsSupported;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new("unavailable", " Not verified: reading the control back failed.");
        }

        if (!supported)
        {
            // Scintilla and similar editors expose no text at all; windows_get_clipboard plus a
            // select-and-copy is the only way to see what they now contain.
            return new("unavailable", " Not verified: this control exposes no value to read back.");
        }
        // Ctrl+V is posted, not processed, so an immediate read almost always still sees the old
        // value. Poll for the change rather than reporting the race as a failed paste.
        var observed = SettledValue(
            () => ReadValue(element),
            before,
            () => Environment.TickCount64,
            () => Thread.Sleep(25),
            OperationContext.Current.Value?.Stop.Token ?? CancellationToken.None,
            settleMilliseconds
        );
        return Judge(before, observed, expected);
    }

    /// <summary>How long the read-back waits for the value to change after Ctrl+V.</summary>
    internal const int SettleMilliseconds = 2000;

    /// <summary>
    /// Read until the value differs from <paramref name="before"/>, <paramref name="settleMilliseconds"/> pass, or the
    /// operation is cancelled or times out (<paramref name="stop"/>); the last value read.
    /// </summary>
    internal static string? SettledValue(
        Func<string?> read,
        string? before,
        Func<long> now,
        Action wait,
        CancellationToken stop,
        int settleMilliseconds = SettleMilliseconds
    )
    {
        var deadline = now() + settleMilliseconds;
        var observed = read();
        while (observed == before && now() < deadline && !stop.IsCancellationRequested)
        {
            wait();
            observed = read();
        }

        return observed;
    }

    /// <summary>The value a control reports, or null when it exposes none or cannot be read.</summary>
    private static string? ReadValue(FlaUI.Core.AutomationElements.AutomationElement? element)
    {
        try
        {
            return element?.Patterns.Value.IsSupported == true
                ? element.Patterns.Value.Pattern.Value.ValueOrDefault
                : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Judge a paste from the value before and after it. A containment test alone reported
    /// "Verified" for an empty paste, for text the control already held, and for a re-paste after
    /// a failed attempt — three ways to confirm a paste that never happened.
    /// </summary>
    /// <param name="before">The value read before Ctrl+V, or null when it could not be read.</param>
    /// <param name="observed">The value read after, or null when it could not be read.</param>
    /// <param name="expected">The text placed on the clipboard.</param>
    /// <returns>A sentence stating what the read-back does and does not establish.</returns>
    internal static string DescribeEvidence(string? before, string? observed, string expected) =>
        Judge(before, observed, expected).Sentence;

    private static string Lines(string text) => text.ReplaceLineEndings("\n");

    /// <summary>The verdict behind <see cref="DescribeEvidence"/>.</summary>
    internal static PasteVerdict Judge(string? before, string? observed, string expected) =>
        JudgeValues(before, observed, expected) with
        {
            Observed = observed,
        };

    private static PasteVerdict JudgeValues(string? before, string? observed, string expected)
    {
        if (observed == null)
        {
            return new("unavailable", " Not verified: this control did not report a value.");
        }

        if (expected.Length == 0)
        {
            return new("not-established", " Not verified: empty text is indistinguishable from no paste.");
        }

        if (before == null)
        {
            return new(
                "not-established",
                " Not verified: the value before the paste could not be read, so a match here "
                    + "cannot be attributed to the paste."
            );
        }

        // Compare without line endings: the clipboard holds CRLF, and a control may report LF.
        (observed, before, expected) = (Lines(observed), Lines(before), Lines(expected));
        if (observed == before)
        {
            // Re-pasting identical selected text leaves the value the same: not evidence, but not a failure.
            return observed.Contains(expected, StringComparison.Ordinal)
                ? new(
                    "unchanged",
                    " Not verified: the control's value did not change, but it already contains the pasted text (re-pasting identical text changes nothing)."
                )
                : new(
                    "not-applied",
                    " Warning: the control's value did not change and does not contain the pasted text, so the paste did not apply."
                );
        }

        if (observed.Contains(expected, StringComparison.Ordinal))
        {
            return new("verified", " Verified: the value changed and now contains the pasted text.");
        }

        return new(
            "mismatch",
            " Warning: the value changed but does not contain the pasted text; the app may have "
                + "reformatted it, or another paste landed first."
        );
    }
}
