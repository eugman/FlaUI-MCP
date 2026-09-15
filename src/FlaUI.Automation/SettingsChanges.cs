using System.Security.Cryptography;

/// <summary>
/// Records TE3 settings files that a run changed outside the four files SettingsLease restores.
/// Only top-level files and the AI folder are compared; WebView2 and icon caches change on every start.
/// Findings are warnings in the manifest until a discovery run shows which changes are normal.
/// </summary>
public static class SettingsChanges
{
    private static readonly string[] Ignored = ["applicationlog.txt"];

    public static Dictionary<string, string> Snapshot(string directory)
    {
        var files = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly);
        var ai = Path.Combine(directory, "AI");
        if (Directory.Exists(ai)) files = files.Concat(Directory.EnumerateFiles(ai, "*", SearchOption.AllDirectories));
        return files
            .Select(path => Path.GetRelativePath(directory, path))
            .Where(relative => !Ignored.Contains(relative, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(relative => relative, relative => Hash(Path.Combine(directory, relative)), StringComparer.OrdinalIgnoreCase);
    }

    public static List<string> Compare(Dictionary<string, string> before, Dictionary<string, string> after) =>
        before.Keys.Union(after.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)
            .Select(name => (before.TryGetValue(name, out var old), after.TryGetValue(name, out var now)) switch
            {
                (true, false) => "removed: " + name,
                (false, true) => "added: " + name,
                _ => before[name] == after[name] ? null : "changed: " + name
            })
            .OfType<string>().ToList();

    private static string Hash(string path)
    {
        // Share read/write: TE3 or another process may hold a settings file open.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
