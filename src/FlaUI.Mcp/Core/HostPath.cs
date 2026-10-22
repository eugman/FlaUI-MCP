using System.Text.RegularExpressions;

namespace FlaUI.Mcp.Core;

/// <summary>
/// Checks for the paths a host reads from its own config (Camtasia's CamConfig, PBI's DemoConfig, TE3's
/// RunConfig) before any is used, and the containment check each host kept a copy of. A configured path must be a
/// local, fully qualified Windows path: no UNC or device prefix, no home (~) or POSIX/WSL form (/mnt/c/...), no
/// . or .. segments, and environment variables either expanded (%NAME%, when defined) or refused.
/// </summary>
public static partial class HostPath
{
    private static readonly char[] Invalid = ['*', '?', '"', '<', '>', '|'];

    [GeneratedRegex(@"%([^%\s]+)%")]
    private static partial Regex WindowsVariable();

    /// <summary>
    /// The full path for a configured <paramref name="path"/>, or an <see cref="ArgumentException"/> that names
    /// <paramref name="field"/> and what is wrong with it.
    /// </summary>
    public static string Validate(string? path, string field = "path")
    {
        var trimmed = Expand(path?.Trim() ?? "", field);
        if (Problem(trimmed) is { } problem)
        {
            throw new ArgumentException($"{field} {problem}");
        }

        return Path.GetFullPath(trimmed);
    }

    /// <summary>
    /// Whether <paramref name="path"/> is <paramref name="root"/> or lies under it, compared as full paths without
    /// regard to case and on a separator boundary (C:\work2 is not under C:\work; C:\work\..\x is not either).
    /// </summary>
    public static bool Within(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd('\\', '/');
        var fullPath = Path.GetFullPath(path).TrimEnd('\\', '/');
        return string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>%NAME% expanded when NAME is defined; an undefined one, or a $NAME / ${NAME} form, is refused.</summary>
    private static string Expand(string path, string field)
    {
        if (path.Contains('$', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"{field} uses a $ environment variable, which is not expanded; write the path out, or use %NAME%."
            );
        }

        foreach (Match variable in WindowsVariable().Matches(path))
        {
            if (Environment.GetEnvironmentVariable(variable.Groups[1].Value) == null)
            {
                throw new ArgumentException(
                    $"{field} uses the environment variable {variable.Value}, which is not defined."
                );
            }
        }

        return Environment.ExpandEnvironmentVariables(path);
    }

    private static string? Problem(string path)
    {
        if (path.Length == 0)
        {
            return "is required.";
        }

        if (path.StartsWith('~'))
        {
            return "must not start with ~: the home directory is not expanded; write the path out.";
        }

        if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
        {
            return "must be a local drive path; UNC and device paths are not supported.";
        }

        if (path.StartsWith('/'))
        {
            return "must be a Windows path (for example C:\\work\\file), not /mnt/... or another POSIX path.";
        }

        if (!Path.IsPathFullyQualified(path))
        {
            return "must be fully qualified, with a drive (for example C:\\work\\file).";
        }

        if (path.IndexOfAny(Invalid) >= 0 || path.IndexOf(':', 2) >= 0)
        {
            return "contains characters that are not valid in a file path.";
        }

        return path.Split('/', '\\').Any(segment => segment is "." or "..")
            ? "must not contain . or .. segments."
            : null;
    }
}
