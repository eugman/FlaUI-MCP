namespace FlaUI.Mcp.Core;

/// <summary>The new process is still alive, but no unique, usable window can be attached safely.</summary>
internal sealed class LaunchPendingException(
    string app,
    int processId,
    IReadOnlyList<Win32WindowInfo> observedWindows,
    bool providerTimedOut
)
    : Exception(
        SessionManager.LaunchFailure(
            app,
            processId,
            exited: false,
            observedWindows.Select(window => window.Title).Where(title => title.Length > 0).ToArray(),
            providerTimedOut
        )
    )
{
    public int ProcessId { get; } = processId;
    public IReadOnlyList<Win32WindowInfo> ObservedWindows { get; } = observedWindows.ToArray();
}
