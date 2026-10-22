using FlaUI.Mcp;
using FlaUI.Mcp.Core;

/// <summary>
/// Scripted Core tool results and failed Core calls, as the TE3 code receives them from Core's registry: one set of
/// builders for every test that feeds TE3 a Core reply, so they all script the same shape.
/// </summary>
internal static class CoreResults
{
    /// <summary>A reply with <paramref name="outcome"/>; failed by default, with Core's text lines.</summary>
    public static McpToolResult Result(ToolOutcome? outcome, bool error = true, params string[] texts) =>
        new()
        {
            IsError = error,
            Content = (texts.Length == 0 ? ["Failed."] : texts).Select(t => new McpContent { Text = t }).ToList(),
            Outcome = outcome,
        };

    /// <summary>A reply with text only and no outcome (an older Core, or a read).</summary>
    public static McpToolResult Text(string text, bool error = false) =>
        new() { IsError = error, Content = [new() { Text = text }] };

    /// <summary>A reply that sent <paramref name="inputs"/> inputs; failed when asked, after they went out.</summary>
    public static McpToolResult Sent(string text, bool failed = false, int inputs = 1) =>
        new()
        {
            IsError = failed,
            Content = [new() { Text = text }],
            Outcome = new ToolOutcome(ToolOutcome.DispatchFor(failed, inputs), "not-tracked")
            {
                Status = failed ? "failed" : "ok",
                InputsSent = inputs,
            },
        };

    /// <summary>A failed Core call as InputTally throws it, with its dispatch, input count and refusal code.</summary>
    public static CoreCallException Failed(
        string dispatch,
        string text = "Failed to click w1e3.",
        int inputsSent = 0,
        string provider = "not-tracked",
        string? code = null,
        string tool = "windows_click"
    ) =>
        new(
            tool,
            new McpToolResult
            {
                IsError = true,
                Content = [new() { Text = text }],
                Outcome = new ToolOutcome(dispatch, provider)
                {
                    Status = "failed",
                    InputsSent = inputsSent,
                    Failure = code == null ? null : new InputRefusal(code, text),
                },
            }
        );

    /// <summary>A failed Core call that guarded input refused with <paramref name="code"/> before sending anything.</summary>
    public static CoreCallException Refused(string code, string text = "Failed to click w1e5: Windows refused.") =>
        Failed("not-dispatched", text, code: code);

    /// <summary>A failed Core call carrying exactly <paramref name="outcome"/>.</summary>
    public static CoreCallException Thrown(
        ToolOutcome outcome,
        string text = "refused",
        string tool = "windows_click"
    ) => new(tool, Result(outcome, true, text));
}
