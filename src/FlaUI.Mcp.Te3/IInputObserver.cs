/// <summary>Counts the user's own keyboard and mouse input.</summary>
internal interface IInputObserver : IDisposable
{
    /// <summary>The running count; throws InvalidOperationException once the observer can no longer vouch for it.</summary>
    long Sequence { get; }
}
