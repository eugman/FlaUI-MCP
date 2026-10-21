using System.Diagnostics;

namespace FlaUI.Mcp.IntegrationTests;

/// <summary>
/// Whether the desktop suite may take the desktop now. Until the machine-wide input lease lands, it refuses
/// while any FlaUI.Mcp host process runs (it may be driving the desktop for a recording) or while
/// FLAUI_DESKTOP_BUSY is set, and it treats as its own only the test apps built under this checkout.
/// </summary>
internal static class DesktopClaim
{
    public const string BusyVariable = "FLAUI_DESKTOP_BUSY";

    /// <summary>Why the desktop is busy, or null when the suite may run.</summary>
    internal static string? BusyReason(IEnumerable<(string Name, int Id)> processes, string? busy, int self)
    {
        if (!string.IsNullOrEmpty(busy))
        {
            return $"{BusyVariable} is set ({busy}); the desktop is in use. Unset it when the desktop is free.";
        }

        var host = processes.FirstOrDefault(p =>
            p.Id != self && p.Name.StartsWith("FlaUI.Mcp", StringComparison.OrdinalIgnoreCase)
        );
        return host.Name == null
            ? null
            : $"{host.Name} (PID {host.Id}) is running and may be driving the desktop; stop it first.";
    }

    /// <summary>Refuse to start the desktop suite while the desktop is busy (a read-only process check).</summary>
    internal static void RequireFree()
    {
        var processes = Process
            .GetProcesses()
            .Select(p =>
            {
                using (p)
                {
                    return (p.ProcessName, p.Id);
                }
            })
            .ToList();
        if (
            BusyReason(processes, Environment.GetEnvironmentVariable(BusyVariable), Environment.ProcessId) is { } reason
        )
        {
            throw new InvalidOperationException($"The desktop tests did not start: {reason}");
        }
    }

    /// <summary>Whether an executable lies under this checkout's test-app folder.</summary>
    internal static bool IsOurTestApp(string? exePath, string testAppsRoot)
    {
        if (string.IsNullOrEmpty(exePath))
        {
            return false;
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(testAppsRoot)) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(exePath).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The executable a process runs, or null when it cannot be read (it is then not ours).</summary>
    internal static string? ExecutableOf(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }
}
