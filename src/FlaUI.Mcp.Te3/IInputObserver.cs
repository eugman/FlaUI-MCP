/// <summary>Counts the user's own keyboard and mouse input.</summary>
internal interface IInputObserver : IDisposable
{
    /// <summary>The running count; throws InvalidOperationException once the observer can no longer vouch for it.</summary>
    long Sequence { get; }

    /// <summary>The user's keyboard events and mouse button-downs, counted apart; throws as <see cref="Sequence"/>.</summary>
    InputCounts Counts { get; }
}

/// <summary>The user's own keyboard events and mouse button-downs so far, and where the last button went down.</summary>
/// <param name="LastButtonDown">The screen point of the last physical button-down, or null before the first.</param>
internal readonly record struct InputCounts(long Keys, long ButtonDowns, System.Drawing.Point? LastButtonDown = null);
