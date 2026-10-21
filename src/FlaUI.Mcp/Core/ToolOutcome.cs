using System.Text.Json.Serialization;

namespace FlaUI.Mcp.Core;

/// <summary>Dispatch and provider lifetimes are separate; pending never authorizes replay.</summary>
public sealed record ToolOutcome(
    [property: JsonPropertyName("dispatch")] string Dispatch,
    [property: JsonPropertyName("provider")] string Provider,
    [property: JsonPropertyName("pendingPatternId")] string? PendingPatternId = null,
    [property: JsonPropertyName("modalTitle")] string? ModalTitle = null,
    [property: JsonPropertyName("operationId")] string? OperationId = null,
    [property: JsonPropertyName("steps"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<ToolStepOutcome>? Steps = null
)
{
    [JsonPropertyName("verification"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Verification { get; init; }

    /// <summary>ok or failed: whether the tool did what was asked, separate from how much input went out.</summary>
    [JsonPropertyName("status"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Status { get; init; }

    /// <summary>Inputs and mutations this call sent: keyboard and mouse input, pattern calls, window changes.</summary>
    [JsonPropertyName("inputsSent"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? InputsSent { get; init; }

    /// <summary>Why guarded input refused, as fields (code, foregroundProcess, target), when it did.</summary>
    [JsonPropertyName("failure"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public InputRefusal? Failure { get; init; }

    /// <summary>How the press point was proven on the element (target, descendant or ancestor), when it was hit-tested.</summary>
    [JsonPropertyName("hitTest"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? HitTest { get; init; }

    /// <summary>The tool's text result, repeated here for clients that only show structuredContent.</summary>
    [JsonPropertyName("message"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; init; }

    [JsonIgnore]
    public bool IsPending => Provider == "pending" || Dispatch == "timed_out_pending";

    /// <summary>completed; partial when it failed after some input went out; otherwise not-dispatched.</summary>
    public static string DispatchFor(bool failed, int inputsSent)
    {
        if (!failed)
        {
            return "completed";
        }

        return inputsSent > 0 ? "partial" : "not-dispatched";
    }

    public static ToolOutcome FromPattern(PatternCallResult result) =>
        new(
            // A call still waiting in validation has not been sent: pending, never safe to replay.
            result.Dispatched
                ? "completed"
                : "timed_out_pending",
            result.Outcome == PatternCallOutcome.Completed ? "returned" : "pending",
            result.PendingPatternId,
            result.ModalTitle,
            OperationContext.Current.Value?.Id
        );
}

public sealed record ToolStepOutcome(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("action")] string? Action,
    [property: JsonPropertyName("isError")] bool IsError,
    [property: JsonPropertyName("outcome")] ToolOutcome? Outcome
);
