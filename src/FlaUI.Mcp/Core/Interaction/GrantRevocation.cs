namespace FlaUI.Mcp.Core;

/// <summary>
/// Why the input observer stopped vouching for the user's input. No key, text, or screen position is retained.
/// </summary>
/// <param name="Kind">heartbeat-gap, thread-exited, disposed, or other (any other failed observation).</param>
/// <param name="GapMs">The heartbeat gap when it latched.</param>
/// <param name="GapAtNextBeatMs">The gap the next beat measured, when one arrived after the latch.</param>
/// <param name="GcPauseMs">Garbage-collection pause time during the gap.</param>
/// <param name="InputDuringGap">Whether Windows recorded any input (injected or not) during the gap; null unread.</param>
public sealed record ObserverFault(
    string Kind,
    long? GapMs = null,
    long? GapAtNextBeatMs = null,
    long? GcPauseMs = null,
    bool? InputDuringGap = null
)
{
    public const string HeartbeatGap = "heartbeat-gap";
    public const string ThreadExited = "thread-exited";
    public const string Disposed = "disposed";
    public const string Other = "other";

    public override string ToString()
    {
        var text = Kind;
        if (GapMs is { } gap)
        {
            text += $" gap {gap} ms";
        }

        if (GapAtNextBeatMs is { } next)
        {
            text += $", next beat {next} ms";
        }

        if (GcPauseMs is { } pause)
        {
            text += $", GC pause {pause} ms";
        }

        if (InputDuringGap is { } input)
        {
            text += input ? ", input during gap" : ", no input during gap";
        }

        return text;
    }
}

/// <summary>The observer can no longer vouch for the user's input; <see cref="Fault"/> says why.</summary>
public sealed class ObserverUnavailableException(ObserverFault fault)
    : InvalidOperationException("Physical input observer is unavailable")
{
    public ObserverFault Fault { get; } = fault;
}

/// <summary>
/// How the last grant ended, kept after the grant so a host can write it down. Privacy-safe: an input kind and
/// mouse subtype, sequences and timings only; never keys, text, or coordinates.
/// </summary>
/// <param name="Cause">user-input, observer-fault, foreground, activation, host-stopped, or end (a normal end).</param>
public sealed record GrantRevocation(
    DateTimeOffset Utc,
    string Code,
    string Reason,
    string Cause,
    long ApprovedSequence,
    long? SnapshotSequence
)
{
    public const string UserInput = "user-input";
    public const string ObserverFailed = "observer-fault";
    public const string Foreground = "foreground";
    public const string Activation = "activation";
    public const string HostStopped = "host-stopped";
    public const string Ended = "end";

    /// <summary>keyboard, mouse or unknown, for the event that coincided with the revocation; null without one.</summary>
    public string? InputKind { get; init; }

    /// <summary>move, button or wheel for a mouse event; null otherwise.</summary>
    public string? MouseKind { get; init; }

    public ObserverFault? Fault { get; init; }

    /// <summary>Physical mouse moves tolerated as drift during the batch.</summary>
    public long ToleratedMoves { get; init; }

    /// <summary>One line for a host's diagnostics.</summary>
    public string Summary()
    {
        var text = $"{Utc:O} grant revoked ({Code}, {Cause}";
        if (Fault != null)
        {
            text += ": " + Fault;
        }

        text += $"): {Reason}; approved sequence {ApprovedSequence}";
        if (SnapshotSequence is { } snapshot)
        {
            text += $", snapshot {snapshot}";
        }

        if (InputKind != null)
        {
            text += $"; last input {InputKind}" + (MouseKind is { } mouse ? "/" + mouse : string.Empty);
        }

        if (ToleratedMoves > 0)
        {
            text += $"; tolerated mouse drift {ToleratedMoves} moves";
        }

        return text;
    }
}
