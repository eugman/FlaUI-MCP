namespace FlaUI.Mcp.Core;

/// <summary>
/// Optional allowlist restricting which applications the MCP tools may automate.
///
/// Configured via the FLAUI_MCP_ALLOWED_APPS environment variable: a semicolon-
/// or comma-separated list of process names, matched case-insensitively and with
/// or without an ".exe" suffix (full paths are reduced to their file name).
/// When the variable is unset or empty, everything is allowed (the historical
/// behavior). When set, the server refuses to launch, register windows for,
/// screenshot, or send input to any process not on the list.
///
/// Matching is by process name, so this is a guard against a misdirected or
/// prompt-injected agent driving unintended apps through this server - not a
/// sandbox against a local attacker who can rename executables.
/// </summary>
public sealed class ProcessPolicy
{
    public const string EnvironmentVariable = "FLAUI_MCP_ALLOWED_APPS";

    private readonly HashSet<string> _allowedNames;

    /// <summary>A policy that allows every application (no allowlist configured).</summary>
    public static ProcessPolicy AllowAll { get; } = new(Array.Empty<string>());

    public ProcessPolicy(IEnumerable<string> allowedApps)
    {
        _allowedNames = allowedApps
            .Select(Normalize)
            .Where(name => name.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static ProcessPolicy FromEnvironment()
    {
        var raw = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return AllowAll;
        }

        return new ProcessPolicy(
            raw.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        );
    }

    /// <summary>True when an allowlist is configured; false means allow everything.</summary>
    public bool IsRestricted => _allowedNames.Count > 0;

    public bool IsNameAllowed(string? processName)
    {
        if (!IsRestricted)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        return _allowedNames.Contains(Normalize(processName));
    }

    /// <summary>
    /// Check a process by id. When restricted, an unknown or exited process is denied.
    /// </summary>
    public bool IsProcessAllowed(int processId)
    {
        if (!IsRestricted)
        {
            return true;
        }

        return IsNameAllowed(NameOf(processId));
    }

    /// <summary>
    /// Check the executable path or name a launch request points at.
    /// </summary>
    public bool IsExecutableAllowed(string appPath)
    {
        if (!IsRestricted)
        {
            return true;
        }

        return IsNameAllowed(appPath);
    }

    public string DescribeDenied(string subject)
    {
        return $"{subject} is not in the FlaUI-MCP app allowlist. "
            + $"Only these apps can be automated: {string.Join(", ", _allowedNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))} "
            + $"(configured via the {EnvironmentVariable} environment variable).";
    }

    public string DescribeAllowed() =>
        IsRestricted
            ? $"app allowlist active: {string.Join(", ", _allowedNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))}"
            : "no app allowlist";

    /// <summary>The refusal for a process or window off the allowlist; nothing was sent.</summary>
    public static Exception NotAllowed(string message) => Refusal(RefusalCodes.ProcessNotAllowed, message, false);

    /// <summary>The refusal for a window whose owning process cannot be read, so it cannot be authorized.</summary>
    public static Exception OwnerUnknown(string message) =>
        Refusal(RefusalCodes.WindowOwnershipUnknown, message, false);

    /// <summary>The refusal for a launch that failed; <paramref name="started"/> when a process did start.</summary>
    public static Exception LaunchFailed(string message, bool started) =>
        Refusal(RefusalCodes.LaunchFailed, message, started);

    /// <summary>The refusal for a window title that matches no top-level window.</summary>
    public static Exception WindowNotFound(string message) => Refusal(RefusalCodes.WindowNotFound, message, false);

    private static Exception Refusal(string code, string message, bool dispatched) =>
        InputRefusedException.Record(new InputRefusal(code, message) { ActionDispatched = dispatched });

    /// <summary>The process's name through the desktop seam, so tests and fakes see the same name the guards do.</summary>
    public static string? NameOf(int processId) => InputDesktop.Current.ProcessNameOf(processId);

    public static string? TryGetProcessName(int processId)
    {
        if (processId <= 0)
        {
            return null;
        }

        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    private static string Normalize(string name)
    {
        var trimmed = name.Trim().Trim('"');
        try
        {
            trimmed = Path.GetFileName(trimmed);
        }
        catch
        { /* keep the raw value if the entry is not path-like */
        }

        if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^4];
        }

        return trimmed;
    }
}
