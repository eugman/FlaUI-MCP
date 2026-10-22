using System.Text.Json.Serialization;

namespace FlaUI.Mcp.Core;

/// <summary>
/// Why guarded input refused to send, as fields a client reads instead of parsing <see cref="Message"/>.
/// The message is kept word for word for clients that still show it.
/// </summary>
public sealed record InputRefusal(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message
)
{
    /// <summary>The process Windows kept in the foreground instead of the target.</summary>
    [JsonPropertyName("foregroundProcess"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProcessRef? ForegroundProcess { get; init; }

    /// <summary>The process the input was meant for.</summary>
    [JsonPropertyName("target"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProcessRef? Target { get; init; }

    /// <summary>
    /// Whether the call that hit this refusal had already sent other input (set on a tool outcome). The
    /// refused step itself never sends anything.
    /// </summary>
    [JsonPropertyName("actionDispatched"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ActionDispatched { get; init; }
}

public sealed record ProcessRef(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("pid")] int Pid
)
{
    /// <summary>untrusted, low, medium, high, system or unknown; set when integrity decided the refusal.</summary>
    [JsonPropertyName("integrityLevel"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IntegrityLevel { get; init; }

    [JsonPropertyName("uiAccess"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? UiAccess { get; init; }
}

/// <summary>Thrown when guarded input sends nothing; carries the structured reason.</summary>
public sealed class InputRefusedException(InputRefusal refusal) : AutomationRefusal(refusal.Code, refusal.Message)
{
    public InputRefusal Refusal { get; } = refusal with { Code = RefusalCodes.Canonical(refusal.Code) };

    /// <summary>
    /// The exception to throw for <paramref name="refusal"/>, also noted on the running operation: tools
    /// turn exceptions into text, and the registry puts the noted refusal back on their outcome.
    /// </summary>
    public static InputRefusedException Record(InputRefusal refusal)
    {
        var exception = new InputRefusedException(refusal) { ActionDispatched = refusal.ActionDispatched == true };
        if (OperationContext.Current.Value is { } operation)
        {
            operation.Refusal = exception.Refusal;
        }

        return exception;
    }

    /// <summary>
    /// The caller caught <paramref name="error"/> and dealt with it (forgot a stale window, captured without a
    /// park): drop its note from the running operation, so a later, unrelated failure in the same call is not
    /// reported under this refusal's code. A newer refusal noted since is kept.
    /// </summary>
    public static void Handled(Exception error)
    {
        if (
            error is InputRefusedException refused
            && OperationContext.Current.Value is { } operation
            && ReferenceEquals(operation.Refusal, refused.Refusal)
        )
        {
            operation.Refusal = null;
        }
    }
}
