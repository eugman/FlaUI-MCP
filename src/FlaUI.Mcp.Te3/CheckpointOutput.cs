/// <summary>
/// Adds a derived checkpoint PNG to a finished run: render, install and verify it before recording it.
/// A crash at any point leaves state a rerun of the same spec completes.
/// </summary>
internal static class CheckpointOutput
{
    /// <summary>Held while a command adds to a run's manifest, so two writers cannot lose each other's entry.</summary>
    public static FileStream Lock(string manifestPath)
    {
        try
        {
            return new FileStream(
                manifestPath + ".lock",
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                1,
                FileOptions.DeleteOnClose
            );
        }
        catch (IOException error) when (error is not (DirectoryNotFoundException or FileNotFoundException))
        {
            throw new IOException("Another command is updating this run's manifest; retry when it finishes.", error);
        }
    }

    /// <summary>Writes output for checkpoint, or accepts what an interrupted earlier attempt left when it is identical.</summary>
    /// <param name="record">Adds the command's own manifest entry; called only when the checkpoint is new.</param>
    public static void Publish(
        string manifestPath,
        RunManifest manifest,
        string checkpoint,
        string output,
        Action<Stream> render,
        Action record
    )
    {
        var temporary = output + ".partial";
        File.Delete(temporary);
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
            {
                render(file);
            }

            var renderedHash = ArtifactFiles.Sha256(temporary);
            var previouslyRecorded = AlreadyRecorded(manifest, checkpoint, output, renderedHash);
            if (!File.Exists(output))
            {
                File.Move(temporary, output);
            }

            // Hold the final file against writes and replacement while its bytes are verified and recorded.
            using var installed = new FileStream(output, FileMode.Open, FileAccess.Read, FileShare.Read);
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(installed));
            if (hash != renderedHash)
            {
                throw new IOException($"{output} already exists with other content; {checkpoint} was not written.");
            }

            if (!previouslyRecorded)
            {
                manifest.Screenshots.Add(checkpoint, output);
                manifest.ScreenshotHashes.Add(checkpoint, hash);
                record();
                AtomicJournal.Write(manifestPath, manifest, RunConfig.Json);
            }
        }
        finally
        {
            File.Delete(temporary);
        }

        // Index rendering uses the actual manifest location, never a stale output path.
        manifest.Output = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
        ArtifactFiles.WriteIndex(manifest);
    }

    // A retry finds its own entry: same path and same pixels. Any other entry for the checkpoint is refused.
    private static bool AlreadyRecorded(RunManifest manifest, string checkpoint, string output, string hash)
    {
        if (!manifest.Screenshots.TryGetValue(checkpoint, out var path))
        {
            return false;
        }

        var samePath = string.Equals(
            Path.GetFullPath(path),
            Path.GetFullPath(output),
            StringComparison.OrdinalIgnoreCase
        );
        if (samePath && manifest.ScreenshotHashes.GetValueOrDefault(checkpoint) == hash)
        {
            return true;
        }

        throw new IOException($"Checkpoint {checkpoint} already exists with other content.");
    }
}
