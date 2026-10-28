namespace FlaUI.Mcp.Core;

/// <summary>A launched process has no unique, usable window that can be attributed safely.</summary>
internal sealed class LaunchPendingException(
    string app,
    int processId,
    IReadOnlyList<Win32WindowInfo> observedWindows,
    bool providerTimedOut,
    bool handoff = false
)
    : Exception(
        SessionManager.LaunchFailure(
            app,
            processId,
            exited: handoff,
            observedWindows.Select(window => window.Title).Where(title => title.Length > 0).ToArray(),
            providerTimedOut
        )
    )
{
    public int ProcessId { get; } = processId;
    public IReadOnlyList<Win32WindowInfo> ObservedWindows { get; } = observedWindows.ToArray();
    public bool Handoff { get; } = handoff;
}
