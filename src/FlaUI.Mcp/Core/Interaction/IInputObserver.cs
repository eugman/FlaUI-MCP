namespace FlaUI.Mcp.Core;

/// <summary>Counts the user's own keyboard and mouse input.</summary>
public interface IInputObserver : IDisposable
{
    /// <summary>The running count; throws InvalidOperationException (an <see cref="ObserverUnavailableException"/>
    /// from the live observer) once the observer can no longer vouch for it.</summary>
    long Sequence { get; }

    /// <summary>The user's keyboard events and mouse button-downs, counted apart; throws as <see cref="Sequence"/>.</summary>
    InputCounts Counts { get; }
}

/// <summary>Privacy-safe metadata about one non-injected event. No key, text, or screen position is retained.</summary>
/// <param name="MouseKind">move, button or wheel for a mouse event; null otherwise.</param>
public sealed record InputEventInfo(long Sequence, DateTimeOffset Utc, string Kind, string? MouseKind = null);

/// <summary>Optional diagnostics supplied by the live low-level hook; test observers need not implement it.</summary>
public interface IInputEventSource
{
    InputEventInfo? LastEvent { get; }

    /// <summary>Why the observer stopped vouching for the input, once it has; readable after it stopped.</summary>
    ObserverFault? Fault => null;

    /// <summary>Physical mouse moves tolerated as drift (not counted) so far; a number only.</summary>
    long ToleratedMoves => 0;
}

/// <summary>The input sequence at grant approval and a later revocation snapshot, which may include newer events.</summary>
public readonly record struct GrantInputEvidence(
    long ApprovedSequence,
    long? SnapshotSequence,
    InputEventInfo? LastEvent
);

/// <summary>The user's own keyboard events and mouse button-downs so far, and where the last button went down.</summary>
/// <param name="LastButtonDown">The screen point of the last physical button-down, or null before the first.</param>
public readonly record struct InputCounts(long Keys, long ButtonDowns, System.Drawing.Point? LastButtonDown = null);
