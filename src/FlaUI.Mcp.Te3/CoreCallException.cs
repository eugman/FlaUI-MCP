using FlaUI.Mcp;
using FlaUI.Mcp.Core;

/// <summary>
/// A Core tool call that failed or is still pending, with Core's outcome for it. The message is the tool's text, for
/// people; code reads <see cref="Outcome"/> (dispatch, inputsSent, failure) instead.
/// </summary>
public sealed class CoreCallException(string? tool, McpToolResult result)
    : InvalidOperationException(string.Join("; ", result.Content.Select(c => c.Text)))
{
    public string? Tool => tool;

    /// <summary>Core's outcome. Core's registry always sets one; a result without it reads as unknown.</summary>
    public ToolOutcome Outcome { get; } = result.Outcome ?? new ToolOutcome("unknown", "unknown");
}

/// <summary>What a failure's structured outcome says about input and the desktop, read instead of its text.</summary>
internal static class CoreFailure
{
    // Refusals that retrying cannot get past: Windows kept another window in front, or there is no desktop.
    private static readonly HashSet<string> DesktopBlockers = ["activation-denied", "desktop-unavailable"];

    /// <summary>
    /// The outcome of the failing Core call, or of a direct send that guarded input refused, searched through the
    /// exceptions that wrap it (a retry, a cancel step).
    /// </summary>
    internal static ToolOutcome? Outcome(Exception? error)
    {
        for (; error != null; error = error.InnerException)
        {
            switch (error)
            {
                case CoreCallException call:
                    return call.Outcome;
                case InputRefusedException refused:
                    return new ToolOutcome("not-dispatched", "not-tracked") { Failure = refused.Refusal };
            }
        }

        return null;
    }

    /// <summary>
    /// Whether this failure itself, not one it wraps, is a Core call whose outcome says nothing was dispatched. What
    /// the caller's own guards changed before it (activation, focus) is in its tally, not here.
    /// </summary>
    internal static bool NotDispatched(Exception error) =>
        error is CoreCallException { Outcome.Dispatch: "not-dispatched" };

    /// <summary>The desktop condition that refused the input, when retrying cannot help: activation-denied or desktop-unavailable.</summary>
    internal static string? Blocker(Exception error) =>
        Outcome(error)?.Failure?.Code is { } code && DesktopBlockers.Contains(code) ? code : null;
}
