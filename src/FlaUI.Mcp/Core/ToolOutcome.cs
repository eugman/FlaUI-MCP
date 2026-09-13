using System.Text.Json.Serialization;

namespace PlaywrightWindows.Mcp.Core;

/// <summary>Dispatch and provider lifetimes are separate; pending never authorizes replay.</summary>
public sealed record ToolOutcome(
    [property: JsonPropertyName("dispatch")] string Dispatch,
    [property: JsonPropertyName("provider")] string Provider,
    [property: JsonPropertyName("pendingPatternId")] string? PendingPatternId = null,
    [property: JsonPropertyName("modalTitle")] string? ModalTitle = null,
    [property: JsonPropertyName("operationId")] string? OperationId = null,
    [property: JsonPropertyName("steps"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<ToolStepOutcome>? Steps = null)
{
    [JsonIgnore]
    public bool IsPending => Provider == "pending" || Dispatch == "timed_out_pending";

    public static ToolOutcome FromPattern(PatternCallResult result) => new("completed",
        result.Outcome == PatternCallOutcome.Completed ? "returned" : "pending", result.PendingPatternId, result.ModalTitle,
        OperationContext.Current.Value?.Id);
}

public sealed record ToolStepOutcome(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("action")] string? Action,
    [property: JsonPropertyName("isError")] bool IsError,
    [property: JsonPropertyName("outcome")] ToolOutcome? Outcome);

internal sealed class PendingProviderException(ToolOutcome outcome)
    : InvalidOperationException("Provider operation still pending; inspect windows_operation_status and handle any dialog explicitly before continuing")
{
    public ToolOutcome Outcome { get; } = outcome;
}
