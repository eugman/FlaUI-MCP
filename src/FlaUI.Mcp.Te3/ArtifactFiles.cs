using System.Diagnostics;
using System.Text.Json;
using FlaUI.Mcp.Core;

public static class ArtifactFiles
{
    public static void WriteIndex(RunManifest manifest)
    {
        static string Encode(string? value) => System.Net.WebUtility.HtmlEncode(value ?? "");
        var html = new System.Text.StringBuilder("<!doctype html><meta charset=utf-8><title>TE3 screenshots</title>");
        html.Append(
            $"<h1>{Encode(manifest.RunId)}</h1><p>Automation: {(manifest.Passed ? "Passed" : "Failed")}. Visual approval is separate; these are candidates.</p>"
        );
        if (!manifest.Passed)
        {
            html.Append(
                $"<p>Step: {Encode(manifest.CurrentStep)}</p><pre>{Encode(manifest.ErrorDetails ?? manifest.Error)}</pre>"
            );
            html.Append(
                $"<p>Cleanup: {Encode(manifest.CleanupError ?? (manifest.NeedsRecovery ? "Recovery required" : "Complete"))}</p>"
            );
            if (File.Exists(Path.Combine(manifest.Output, "failure.png")))
            {
                html.Append("<h2>Failure capture</h2><img style='max-width:100%' src='failure.png'>");
            }
        }
        // The index is the artifact a human reads, so a settings change has to be visible here and
        // not only in manifest.json.
        if (manifest.SettingsChanges.Count > 0)
        {
            html.Append("<h2>Settings changed by this run</h2><ul>");
            foreach (var change in manifest.SettingsChanges)
            {
                html.Append($"<li>{Encode(change)}</li>");
            }

            html.Append(
                "</ul><p>These files differ from before the run; restore them before relying on the result.</p>"
            );
        }

        foreach (var checkpoint in manifest.Screenshots.Keys)
        {
            var path = ContainedPath(manifest.Screenshots[checkpoint], manifest.Output);
            var file = string.Join(
                "/",
                Path.GetRelativePath(manifest.Output, path)
                    .Split(Path.DirectorySeparatorChar)
                    .Select(Uri.EscapeDataString)
            );
            html.Append($"<h2>{Encode(checkpoint)}</h2><a href='{file}'><img style='max-width:100%' src='{file}'></a>");
            var original = OriginalFor(checkpoint, manifest.DocsRoot);
            if (original != null)
            {
                html.Append(
                    $"<p>Original in the docs:</p><img style='max-width:100%' src='{Encode(new Uri(original).AbsoluteUri)}'>"
                );
            }

            if (manifest.Compositions.ContainsKey(checkpoint))
            {
                html.Append("<p>Derived composition: crop/annotations; source captures retained.</p>");
            }
        }

        File.WriteAllText(Path.Combine(manifest.Output, "index.html"), html.ToString());
    }

    /// <summary>The original docs image for a checkpoint named after a backlog item, if the docs copy has it.</summary>
    internal static string? OriginalFor(string checkpoint, string? docsRoot, string? backlogPath = null)
    {
        if (docsRoot == null || !Backlog.IsItemId(checkpoint))
        {
            return null;
        }

        try
        {
            var item = Backlog.Find(checkpoint, backlogPath);
            if (!item.Source.StartsWith("content/", StringComparison.Ordinal))
            {
                return null;
            }

            var path = ContainedPath(item.Source, docsRoot);
            return File.Exists(path) ? path : null;
        }
        catch (Exception error) when (error is ArgumentException or IOException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Promotes the checkpoint named after a backlog item to that item's docs image path. When the item was annotated, the
    /// annotated image goes to the docs path and the clean capture and spec go beside it (NAME.clean.png,
    /// NAME.annotations.json) so it can be re-annotated by hand. A redacted item's clean capture is never promoted.
    /// </summary>
    public static async Task PromoteItem(string runDirectory, string itemId, bool overwrite, string? backlogPath = null)
    {
        var manifest = ReadManifest(Path.Combine(runDirectory, "manifest.json"));
        var root = manifest.DocsRoot ?? throw new ArgumentException("Set docsRoot in the run config before promotion");
        var backlogItem = Backlog.Find(itemId, backlogPath);
        var image = Path.Combine(root, Backlog.DocsImage(backlogItem));
        var annotation = manifest.Annotations.Values.SingleOrDefault(a => a.Source == itemId);
        // The clean capture of an item that needs redaction shows what the redaction exists to hide,
        // so publishing it is the failure this flag prevents. Refuse rather than promote it.
        if (backlogItem.RequiresRedaction && annotation is not { Redacted: true })
        {
            throw new InvalidOperationException(
                $"{itemId} is marked requiresRedaction, so only a redacted annotation may be promoted. "
                    + (
                        annotation == null
                            ? "This run has no annotation for it; annotate it first."
                            : "Its annotation has no redactions; add them before promoting."
                    )
            );
        }
        // The redaction requirement is already satisfied above, so these promotions say so
        // rather than re-deriving it and refusing the redacted image they were asked to publish.
        if (annotation == null)
        {
            await PromoteCore(runDirectory, itemId, image, overwrite, null, redactionSatisfied: true);
            return;
        }

        string? spec = null;
        if (!annotation.Redacted)
        {
            spec = ContainedPath(Path.ChangeExtension(image, ".annotations.json"), root);
            if (File.Exists(spec))
            {
                if (!overwrite)
                {
                    throw new IOException("Destination exists; explicit --overwrite required");
                }

                await RequireCleanTracked(root, spec);
            }
        }

        await PromoteCore(runDirectory, annotation.Checkpoint, image, overwrite, null, redactionSatisfied: true);
        if (annotation.Redacted)
        {
            return;
        }

        await PromoteCore(
            runDirectory,
            itemId,
            Path.ChangeExtension(image, ".clean.png"),
            overwrite,
            null,
            redactionSatisfied: true
        );
        AtomicJournal.Write(spec!, annotation, new JsonSerializerOptions(RunConfig.Json) { WriteIndented = true });
    }

    /// <summary>
    /// Whether a checkpoint names a backlog item whose capture must not be published unredacted.
    /// A checkpoint that is not an item id is not covered; a backlog that cannot be read at all
    /// throws rather than answering "no", since that answer would publish the image.
    /// </summary>
    /// <param name="checkpoint">Checkpoint name, which may be a backlog item id.</param>
    /// <param name="backlogPath">Backlog location, or null for the default.</param>
    /// <returns>True when the item requires redaction.</returns>
    internal static bool RequiresRedaction(string checkpoint, string? backlogPath)
    {
        if (!Backlog.IsItemId(checkpoint))
        {
            return false;
        }

        try
        {
            return Backlog.Find(checkpoint, backlogPath).RequiresRedaction;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static string ContainedPath(string path, string root)
    {
        root = Path.GetFullPath(root);
        var full = Path.GetFullPath(path, root);
        // Core's containment check; the root itself is not a path under it.
        if (!HostPath.Within(root, full) || HostPath.Within(full, root))
        {
            throw new ArgumentException("Path escapes the configured root");
        }

        for (var current = full; current != null; current = Path.GetDirectoryName(current))
        {
            if (
                (File.Exists(current) || Directory.Exists(current))
                && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0
            )
            {
                throw new IOException("Reparse points are not supported for artifact operations");
            }
        }

        return full;
    }

    public static string Sha256(string path) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));

    public static RunManifest ReadManifest(string path)
    {
        var json = File.ReadAllText(path);
        // SchemaVersion defaults to the current version, so a manifest without one would silently claim it.
        using (var document = JsonDocument.Parse(json))
        {
            if (
                document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.EnumerateObject().Any(p => p.NameEquals("schemaVersion"))
            )
            {
                throw new InvalidDataException(
                    $"{path} has no schemaVersion; refusing to guess which runner wrote it."
                );
            }
        }

        return JsonSerializer.Deserialize<RunManifest>(json, RunConfig.Json)
            ?? throw new ArgumentException("Missing manifest");
    }

    public static Task Promote(
        string runDirectory,
        string checkpoint,
        string destination,
        bool overwrite,
        string? backlogPath = null
    ) => PromoteCore(runDirectory, checkpoint, destination, overwrite, backlogPath, redactionSatisfied: false);

    private static async Task PromoteCore(
        string runDirectory,
        string checkpoint,
        string destination,
        bool overwrite,
        string? backlogPath,
        bool redactionSatisfied
    )
    {
        // The guard used to live only in PromoteItem, but Promote is public and is reachable from
        // the supported command line as `promote <run> L247 <docs path>`, which published the
        // clean About capture — licence holder, installations and session id — with no error.
        if (!redactionSatisfied && RequiresRedaction(checkpoint, backlogPath))
        {
            throw new InvalidOperationException(
                $"{checkpoint} is marked requiresRedaction, so it cannot be promoted directly. "
                    + "Promote it with --item so its redacted annotation is used."
            );
        }

        var manifest = ReadManifest(Path.Combine(runDirectory, "manifest.json"));
        if (!manifest.Passed || manifest.NeedsRecovery)
        {
            throw new ArgumentException("Promotion requires a passed, cleaned-up run");
        }

        if (!manifest.Screenshots.TryGetValue(checkpoint, out var source))
        {
            throw new ArgumentException("Unknown checkpoint");
        }

        source = ContainedPath(source, runDirectory);
        // Older manifests have no hashes; newer ones must still match the captured bytes.
        if (manifest.ScreenshotHashes.TryGetValue(checkpoint, out var captured) && Sha256(source) != captured)
        {
            throw new IOException("Checkpoint PNG changed since capture; refusing promotion");
        }

        var root = manifest.DocsRoot ?? throw new ArgumentException("Set docsRoot in the run config before promotion");
        if (
            Path.GetFullPath(root)
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(segment => segment.Equals("TabularEditorDocs", StringComparison.OrdinalIgnoreCase))
        )
        {
            throw new ArgumentException("The original TabularEditorDocs repository is read-only; choose a docs copy");
        }

        destination = ContainedPath(destination, root);
        if (!destination.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Destination must be PNG");
        }

        if (File.Exists(destination))
        {
            if (!overwrite)
            {
                throw new IOException("Destination exists; explicit --overwrite required");
            }

            await RequireCleanTracked(root, destination);
        }

        CopyVerified(source, destination, captured, overwrite);
    }

    internal static void CopyVerified(string source, string destination, string? captured, bool overwrite)
    {
        var folder = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(folder);
        var staged = Path.Combine(folder, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.Copy(source, staged);
            if (captured is not null && Sha256(staged) != captured)
            {
                throw new IOException("Checkpoint PNG changed since capture; refusing promotion");
            }

            File.Move(staged, destination, overwrite);
        }
        finally
        {
            if (File.Exists(staged))
            {
                File.Delete(staged);
            }
        }
    }

    private static async Task RequireCleanTracked(string root, string path)
    {
        async Task<int> Git(params string[] args)
        {
            var start = new ProcessStartInfo("git")
            {
                WorkingDirectory = root,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var arg in args)
            {
                start.ArgumentList.Add(arg);
            }

            return await RunGitCheck(start);
        }

        var relative = Path.GetRelativePath(root, path);
        if (
            await Git("ls-files", "--error-unmatch", "--", relative) != 0
            || await Git("diff", "--quiet", "HEAD", "--", relative) != 0
        )
        {
            throw new IOException("Refusing to overwrite an untracked or locally modified destination");
        }
    }

    internal static async Task<int> RunGitCheck(ProcessStartInfo start, TimeSpan? timeLimit = null)
    {
        // Raw on purpose: git only reads the repository's index here and changes neither the desktop nor TE3, so it
        // needs no input lease or grant (Core's GuardedDesktop.StartProcess is for starts that change something).
        using var process = Process.Start(start) ?? throw new IOException("Git did not start");
        using var timeout = new CancellationTokenSource(timeLimit ?? TimeSpan.FromSeconds(30));
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            // Await both readers with the exit wait so every failure is observed.
            await Task.WhenAll(process.WaitForExitAsync(timeout.Token), stdout, stderr);
            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await process.WaitForExitAsync(shutdown.Token);
            }
            catch (Exception error)
            {
                throw new TimeoutException(
                    $"Git check timed out; shutdown of PID {process.Id} could not be confirmed. Destination was not overwritten.",
                    error
                );
            }

            throw new TimeoutException(
                $"Git check timed out and PID {process.Id} was terminated. Destination was not overwritten."
            );
        }
    }
}
