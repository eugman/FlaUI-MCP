using System.Drawing;
using System.Text.Json;
using System.Text.Json.Serialization;
using FlaUI.Mcp.Core;

namespace FlaUI.Mcp.Tools;

/// <summary>windows_mouse's reply: where the pointer landed, what the tool changed first, what a drag found.</summary>
internal static class MouseReplies
{
    // What the tool itself changed before the input (a scroll into view) is reported only when it did.
    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// A drag whose drop target moved: Escape and the release went out, so the call is unverified with the
    /// refusal as its failure, not failed; the text is the refusal's.
    /// </summary>
    public static McpToolResult Unverified(InputRefusal refusal) =>
        new()
        {
            Content = [new() { Type = "text", Text = refusal.Message }],
            Outcome = new ToolOutcome("completed", "not-tracked")
            {
                Verification = "not-established",
                Failure = refusal,
            },
        };

    /// <summary>
    /// The landing point in screen pixels and relative to the window's capture origin; for a drag, what UI
    /// Automation reported under the pointer after the release (droppedOn) and, for a resizeTo drag, the size the
    /// ref reached against the one requested, as the outcome's proof: a size that differs is a failure.
    /// </summary>
    public static McpToolResult Landed(
        string action,
        Point landed,
        Rectangle origin,
        string? changed,
        DragReport? drag = null
    )
    {
        var resized = drag?.Resized;
        var text = JsonSerializer.Serialize(
            new
            {
                action,
                screen = new { x = landed.X, y = landed.Y },
                window = new { x = landed.X - origin.X, y = landed.Y - origin.Y },
                changed,
                droppedOn = drag?.DroppedOn,
                resized = resized == null ? null : new { requested = resized.Expected, reached = resized.Observed },
            },
            Options
        );
        var mismatch = resized?.Verdict == Proof.Mismatch;
        return new McpToolResult
        {
            Content =
            [
                new()
                {
                    Type = "text",
                    Text = mismatch ? $"Released, but the element did not reach the requested size: {text}" : text,
                },
            ],
            IsError = mismatch ? true : null,
            Outcome = resized == null ? null : new ToolOutcome("completed", "not-tracked").WithProof(resized),
        };
    }
}
