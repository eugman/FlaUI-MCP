namespace FlaUI.Mcp.Core;

/// <summary>
/// How a poll ended, so "done" and "gave up" never share a null or false: <see cref="Done"/> with the value,
/// <see cref="TimedOut"/> with the last value read and the last read error, or <see cref="Changed"/> when
/// something else happened first (such as a modal dialog opening).
/// </summary>
public abstract record WaitOutcome<T>
{
    private WaitOutcome() { }

    public sealed record Done(T Value) : WaitOutcome<T>;

    /// <summary>The deadline passed. <paramref name="LastError"/> is the last read that failed, if any.</summary>
    public sealed record TimedOut(T? Last, Exception? LastError) : WaitOutcome<T>;

    public sealed record Changed(string What) : WaitOutcome<T>;
}
