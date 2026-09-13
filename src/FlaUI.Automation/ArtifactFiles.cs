using System.Diagnostics;
using System.Text.Json;

public static class ArtifactFiles
{
    public static void WriteIndex(RunManifest manifest)
    {
        static string Encode(string? value) => System.Net.WebUtility.HtmlEncode(value ?? "");
        var html = new System.Text.StringBuilder("<!doctype html><meta charset=utf-8><title>TE3 screenshots</title>");
        html.Append($"<h1>{Encode(manifest.RunId)}</h1><p>Automation: {(manifest.Passed ? "Passed" : "Failed")}. Visual approval is separate; these are candidates.</p>");
        if (!manifest.Passed)
        {
            html.Append($"<p>Step: {Encode(manifest.CurrentStep)}</p><pre>{Encode(manifest.ErrorDetails ?? manifest.Error)}</pre>");
            html.Append($"<p>Cleanup: {Encode(manifest.CleanupError ?? (manifest.NeedsRecovery ? "Recovery required" : "Complete"))}</p>");
            if (File.Exists(Path.Combine(manifest.Output, "failure.png")))
                html.Append("<h2>Failure capture</h2><img style='max-width:100%' src='failure.png'>");
        }
        foreach (var checkpoint in manifest.Screenshots.Keys)
        {
            var path = ContainedPath(manifest.Screenshots[checkpoint], manifest.Output);
            var file = string.Join("/", Path.GetRelativePath(manifest.Output, path).Split(Path.DirectorySeparatorChar).Select(Uri.EscapeDataString));
            html.Append($"<h2>{Encode(checkpoint)}</h2><a href='{file}'><img style='max-width:100%' src='{file}'></a>");
            if (manifest.Compositions.ContainsKey(checkpoint)) html.Append("<p>Derived composition: crop/annotations; source captures retained.</p>");
        }
        File.WriteAllText(Path.Combine(manifest.Output, "index.html"), html.ToString());
    }

    public static string ContainedPath(string path, string root)
    {
        root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(path, root);
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Path escapes the configured root");
        for (var current = full; current != null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Reparse points are not supported for artifact operations");
        return full;
    }
    public static RunManifest ReadManifest(string path) => JsonSerializer.Deserialize<RunManifest>(File.ReadAllText(path), RunConfig.Json)
        ?? throw new ArgumentException("Missing manifest");

    public static async Task Promote(string runDirectory, string checkpoint, string destination, bool overwrite)
    {
        var manifest = ReadManifest(Path.Combine(runDirectory, "manifest.json"));
        if (!manifest.Passed || manifest.NeedsRecovery) throw new ArgumentException("Promotion requires a passed, cleaned-up run");
        if (!manifest.Screenshots.TryGetValue(checkpoint, out var source)) throw new ArgumentException("Unknown checkpoint");
        source = ContainedPath(source, runDirectory);
        var root = manifest.DocsRoot ?? throw new ArgumentException("Set docsRoot in the run config before promotion");
        if (Path.GetFullPath(root).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment.Equals("TabularEditorDocs", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("The original TabularEditorDocs repository is read-only; choose a docs copy");
        destination = ContainedPath(destination, root);
        if (!destination.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Destination must be PNG");
        if (File.Exists(destination))
        {
            if (!overwrite) throw new IOException("Destination exists; explicit --overwrite required");
            await RequireCleanTracked(root, destination);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite);
    }
    private static async Task RequireCleanTracked(string root, string path)
    {
        async Task<int> Git(params string[] args)
        {
            var start = new ProcessStartInfo("git") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in args) start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(); await stdout; await stderr;
            return process.ExitCode;
        }
        var relative = Path.GetRelativePath(root, path);
        if (await Git("ls-files", "--error-unmatch", "--", relative) != 0 ||
            await Git("diff", "--quiet", "HEAD", "--", relative) != 0)
            throw new IOException("Refusing to overwrite an untracked or locally modified destination");
    }
}
