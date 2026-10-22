using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

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

    /// <summary>How the tool checked its effect, when it did; status follows its verdict.</summary>
    [JsonPropertyName("proof"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Proof? Proof { get; init; }

    // One copy of the caller's entries: handed out read-only, and serialised from the dictionary behind it.
    private readonly Dictionary<string, JsonElement>? detailFields;
    private readonly ReadOnlyDictionary<string, JsonElement>? details;

    /// <summary>
    /// A host's own structured fields, such as TE3's <c>walk</c>, written at the top level of structuredContent beside
    /// the fixed fields. Keys are camelCase names and never one of the fixed fields; the dictionary is copied.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyDictionary<string, JsonElement>? Details
    {
        get => details;
        init => (detailFields, details) = CheckDetails(value);
    }

    // Serialised flat, as the extension data of the outcome; only the read-only view is ever handed out.
    [JsonExtensionData, JsonInclude]
    private Dictionary<string, JsonElement>? DetailFields
    {
        get => detailFields;
        init => (detailFields, details) = CheckDetails(value is { Count: > 0 } ? value : null);
    }

    private static readonly HashSet<string> FixedFields = typeof(ToolOutcome)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Select(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name)
        .OfType<string>()
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static (Dictionary<string, JsonElement>?, ReadOnlyDictionary<string, JsonElement>?) CheckDetails(
        IReadOnlyDictionary<string, JsonElement>? value
    )
    {
        if (value is null)
        {
            return (null, null);
        }

        foreach (var key in value.Keys)
        {
            if (FixedFields.Contains(key))
            {
                throw new ArgumentException($"Outcome detail '{key}' would replace the fixed field of that name.");
            }

            if (!Regex.IsMatch(key, "^[a-z][A-Za-z0-9]*$", RegexOptions.None, TimeSpan.FromSeconds(1)))
            {
                throw new ArgumentException($"Outcome detail '{key}' is not a camelCase name.");
            }
        }

        var copy = new Dictionary<string, JsonElement>(value);
        return (copy, copy.AsReadOnly());
    }

    /// <summary>This outcome with <paramref name="proof"/>, its verdict also given as the verification.</summary>
    public ToolOutcome WithProof(Proof proof) => this with { Proof = proof, Verification = proof.Verdict };

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
