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
}

public sealed record ProcessRef(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("pid")] int Pid
);

/// <summary>Thrown when guarded input sends nothing; carries the structured reason.</summary>
public sealed class InputRefusedException(InputRefusal refusal) : InvalidOperationException(refusal.Message)
{
    public InputRefusal Refusal { get; } = refusal;
}
