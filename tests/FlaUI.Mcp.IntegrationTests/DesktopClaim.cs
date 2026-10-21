using System.Diagnostics;

namespace FlaUI.Mcp.IntegrationTests;

/// <summary>
/// Whether the desktop suite may take the desktop now. Until the machine-wide input lease lands, it refuses
/// while any FlaUI.Mcp host process runs (it may be driving the desktop for a recording) or while
/// FLAUI_DESKTOP_BUSY is set, and it treats as its own only the test apps built under this checkout.
/// </summary>
/// <summary>A running process as the busy check sees it; Modules is read only for dotnet processes.</summary>
internal sealed record ProcessProbe(string Name, int Id, IReadOnlyList<string> Modules);

internal static class DesktopClaim
{
    public const string BusyVariable = "FLAUI_DESKTOP_BUSY";

    /// <summary>Why the desktop is busy, or null when the suite may run.</summary>
    internal static string? BusyReason(IEnumerable<ProcessProbe> processes, string? busy, int self)
    {
        if (!string.IsNullOrEmpty(busy))
        {
            return $"{BusyVariable} is set ({busy}); the desktop is in use. Unset it when the desktop is free.";
        }

        foreach (var process in processes.Where(p => p.Id != self))
        {
            if (IsHostName(process.Name))
            {
                return $"{process.Name} (PID {process.Id}) is running and may be driving the desktop; stop it first.";
            }

            // A host started as `dotnet FlaUI.Mcp.X.dll` runs under the name dotnet.
            if (process.Modules.Select(Path.GetFileName).FirstOrDefault(IsHostAssembly) is { } assembly)
            {
                return $"{process.Name} (PID {process.Id}) has {assembly} loaded and may be driving the desktop; stop it first.";
            }
        }

        return null;
    }

    private static bool IsHostName(string name) => name.StartsWith("FlaUI.Mcp", StringComparison.OrdinalIgnoreCase);

    private static bool IsHostAssembly(string? file) =>
        file != null
        && file.StartsWith("FlaUI.Mcp", StringComparison.OrdinalIgnoreCase)
        && file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

    /// <summary>Refuse to start the desktop suite while the desktop is busy (a read-only process check).</summary>
    internal static void RequireFree()
    {
        var processes = Process
            .GetProcesses()
            .Select(p =>
            {
                using (p)
                {
                    return new ProcessProbe(p.ProcessName, p.Id, p.ProcessName == "dotnet" ? ModulesOf(p) : []);
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

    /// <summary>
    /// The files a process has loaded (a read-only query), or none when they cannot be read. This fails open for
    /// elevated dotnet hosts: a non-elevated test host cannot read an elevated process's modules, so such a host is
    /// not seen. Test-only code; the machine-wide lease (Stage B) replaces this check.
    /// </summary>
    private static IReadOnlyList<string> ModulesOf(Process process)
    {
        try
        {
            return process.Modules.Cast<ProcessModule>().Select(module => module.FileName).ToList();
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return [];
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
