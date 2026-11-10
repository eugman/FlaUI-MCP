using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

public sealed class SettingsLease
{
    public static readonly string[] Names =
    {
        "Preferences.json",
        "UiPreferences.json",
        "Layouts.json",
        "RecentFiles.json",
    };
    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TabularEditor3");
    public static string DefaultBackupRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FlaUI-MCP",
            "settings-backups"
        );
    public string BackupDirectory { get; }
    private readonly string directory;
    private BackupInfo info;
    private bool restoreVerified;

    public static void Validate(string? directory = null)
    {
        var path = Path.Combine(directory ?? DefaultDirectory, "UiPreferences.json");
        try
        {
            if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject)
            {
                throw new JsonException("Expected object");
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"TE3 preferences unavailable or malformed: {path}. Open TE3 once, initialize/repair preferences, and close it before testing.",
                ex
            );
        }
    }

    public SettingsLease(string? directory = null, string? backupDirectory = null, string? manifestPath = null)
    {
        this.directory = directory ?? DefaultDirectory;
        Validate(this.directory);
        BackupDirectory =
            backupDirectory ?? Path.Combine(DefaultBackupRoot, "fla_settings_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(BackupDirectory);
        var hashes = new Dictionary<string, string?>();
        foreach (var name in Names)
        {
            var path = Path.Combine(this.directory, name);
            hashes[name] = File.Exists(path) ? Hash(path) : null;
            if (File.Exists(path))
            {
                File.Copy(path, Path.Combine(BackupDirectory, name), false);
            }
        }

        info = new BackupInfo(this.directory, hashes, Manifest: manifestPath);
        WriteInfo();
    }

    public void Normalize(bool maximize = false)
    {
        var path = Path.Combine(directory, "UiPreferences.json");
        var settings = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        settings["Skin"] = "Basic";
        settings["SkinPalette"] = "";
        settings["Language"] = "en-US";
        settings["MainWindowState"] = maximize ? "Maximized" : "Normal";
        settings["RememberWindowBounds"] = true;
        settings["RememberWindowState"] = true;
        settings["MainWindowRectangle"] = new JsonObject
        {
            ["X"] = 40,
            ["Y"] = 40,
            ["W"] = 1400,
            ["H"] = 900,
        };
        var bytes = Encoding.UTF8.GetBytes(settings.ToJsonString());
        // Recorded before the write, so a crash between them leaves the original, which recover also accepts.
        var normalized = CurrentHashes(directory);
        normalized["UiPreferences.json"] = Convert.ToHexString(SHA256.HashData(bytes));
        info = info with { Normalized = normalized };
        WriteInfo();
        WriteAtomically(path, temporary => File.WriteAllBytes(temporary, bytes));
    }

    public void Restore()
    {
        RestoreBackup(BackupDirectory, directory);
        restoreVerified = true;
    }

    /// <summary>
    /// Removes this lease's backup once <see cref="Restore"/> has succeeded and been verified. Call it only after
    /// anything that still points at the backup (the run manifest) no longer needs it. Best effort.
    /// </summary>
    /// <returns>True when the backup directory is gone.</returns>
    public bool DiscardBackup() => restoreVerified && TryDeleteBackup(BackupDirectory, directory);

    /// <summary>
    /// Deletes a backup directory, but only a directory holding nothing except the leased file names and
    /// restore.json: never the settings folder, the backup root, a drive root or a folder with other content.
    /// Never throws; a backup that cannot be removed is simply kept.
    /// </summary>
    internal static bool TryDeleteBackup(string backup, string settingsDirectory, string? backupRoot = null)
    {
        try
        {
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(backup));
            if (
                IsProtected(full, settingsDirectory, backupRoot ?? DefaultBackupRoot)
                || !File.Exists(Path.Combine(full, InfoName))
            )
            {
                return false;
            }

            var owned = new HashSet<string>(Names.Append(InfoName), StringComparer.OrdinalIgnoreCase);
            var entries = Directory.GetFileSystemEntries(full);
            if (entries.Any(entry => !IsOwnedFile(entry, owned)))
            {
                return false;
            }

            // restore.json first: the restore is already verified, and without it a delete that stops part-way
            // leaves a folder recover reports as gone, not a manifest naming files that are missing.
            foreach (var entry in entries.OrderByDescending(IsInfo))
            {
                File.Delete(entry);
            }

            Directory.Delete(full);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private const string InfoName = "restore.json";

    private static bool IsInfo(string entry) =>
        Path.GetFileName(entry).Equals(InfoName, StringComparison.OrdinalIgnoreCase);

    /// <summary>A drive root, the settings folder or the backup root: never a backup to delete.</summary>
    internal static bool IsProtected(string full, string settingsDirectory, string backupRoot) =>
        string.Equals(Path.GetPathRoot(full), full, StringComparison.OrdinalIgnoreCase)
        || SamePath(full, settingsDirectory)
        || SamePath(full, backupRoot);

    private static bool IsOwnedFile(string entry, HashSet<string> owned) =>
        owned.Contains(Path.GetFileName(entry))
        && (File.GetAttributes(entry) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0;

    private static bool SamePath(string a, string b) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(a))
            .Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The recover command: restores an interrupted run's backup, refusing (unless forced) when the settings
    /// differ from what the run left, then marks the run's manifest recovered.
    /// </summary>
    /// <returns>True when the run's manifest was marked recovered.</returns>
    public static bool Recover(string backup, bool force, string? expectedDirectory = null)
    {
        var info = ReadInfo(backup, expectedDirectory);
        var changed = ChangedSinceRun(info);
        if (!force && changed.Count > 0)
        {
            throw new InvalidOperationException(
                $"TE3 settings changed after the run normalized them: {string.Join(", ", changed)}. The run's own TE3 "
                    + "may have written them, or they were changed since. Check them, then rerun recover with --force "
                    + "to put back the pre-run backup; nothing was restored."
            );
        }

        RestoreBackup(backup, expectedDirectory);
        var marked = MarkRecovered(info.Manifest, backup);
        // Gone only when nothing records a run still needing it: no manifest, or this run's manifest now says restored.
        var unneeded = marked || info.Manifest == null || !File.Exists(info.Manifest);
        if (unneeded && TryDeleteBackup(backup, info.Directory) && marked)
        {
            var manifest = ArtifactFiles.ReadManifest(info.Manifest!);
            manifest.SettingsBackupRemoved = true;
            AtomicJournal.Write(info.Manifest!, manifest, RunConfig.Json);
        }

        return marked;
    }

    // Each file must still be either its pre-run original or what Normalize wrote.
    private static List<string> ChangedSinceRun(BackupInfo info)
    {
        var current = CurrentHashes(info.Directory);
        return Names
            .Where(name =>
                current[name] != info.Hashes[name]
                && !(
                    info.Normalized != null && info.Normalized.TryGetValue(name, out var left) && current[name] == left
                )
            )
            .ToList();
    }

    private static bool MarkRecovered(string? manifestPath, string backup)
    {
        if (manifestPath == null || !File.Exists(manifestPath))
        {
            return false;
        }

        var manifest = ArtifactFiles.ReadManifest(manifestPath);
        if (
            manifest.SettingsBackup == null
            || !Path.GetFullPath(manifest.SettingsBackup)
                .Equals(Path.GetFullPath(backup), StringComparison.OrdinalIgnoreCase)
        )
        {
            return false;
        }

        // One atomic write: no reader sees the settings restored while the run still needs recovery, or the reverse.
        manifest.SettingsRestored = true;
        manifest.NeedsRecovery = false;
        AtomicJournal.Write(manifestPath, manifest, RunConfig.Json);
        return true;
    }

    private static Dictionary<string, string?> CurrentHashes(string directory) =>
        Names.ToDictionary(
            name => name,
            name => File.Exists(Path.Combine(directory, name)) ? Hash(Path.Combine(directory, name)) : null
        );

    private void WriteInfo() =>
        AtomicJournal.Write(Path.Combine(BackupDirectory, "restore.json"), info, JsonSerializerOptions.Default);

    private static BackupInfo ReadInfo(string backup, string? expectedDirectory)
    {
        var infoPath = Path.Combine(backup, "restore.json");
        if (!File.Exists(infoPath))
        {
            throw new InvalidOperationException(
                $"No settings backup at {backup}: it was already restored and removed, or it never existed. Nothing was restored."
            );
        }

        var info = JsonSerializer.Deserialize<BackupInfo>(File.ReadAllText(infoPath))!;
        if (
            !Path.GetFullPath(info.Directory)
                .Equals(Path.GetFullPath(expectedDirectory ?? DefaultDirectory), StringComparison.OrdinalIgnoreCase)
            || info.Hashes.Count != Names.Length
            || info.Hashes.Keys.Except(Names).Any()
        )
        {
            throw new InvalidOperationException("Invalid recovery targets");
        }

        return info;
    }

    // Write beside the target, then replace it, so a crash never leaves a half-written settings file.
    internal static void WriteAtomically(string target, Action<string> write)
    {
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            write(temporary);
            File.Move(temporary, target, true);
        }
        catch
        {
            DeleteQuietly(temporary);
            throw;
        }
    }

    // Best effort after a failed write: the write's own error is the one to report.
    private static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left behind next to the target; the next write uses a new name.
        }
    }

    public static void RestoreBackup(string backup, string? expectedDirectory = null)
    {
        var info = ReadInfo(backup, expectedDirectory);

        foreach (var (name, hash) in info.Hashes)
        {
            if (hash != null && Hash(Path.Combine(backup, name)) != hash)
            {
                throw new IOException($"Backup checksum mismatch: {name}");
            }
        }

        var errors = new List<Exception>();
        foreach (var (name, hash) in info.Hashes)
        {
            try
            {
                var target = Path.Combine(info.Directory, name);
                if (hash == null)
                {
                    if (File.Exists(target))
                    {
                        File.Delete(target);
                    }
                }
                else
                {
                    WriteAtomically(target, temporary => File.Copy(Path.Combine(backup, name), temporary));
                    if (Hash(target) != hash)
                    {
                        throw new IOException($"Restoration hash mismatch: {name}");
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        }

        if (errors.Count != 0)
        {
            throw new AggregateException("Partial restoration; retain backup and retry recovery", errors);
        }
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    /// <param name="Hashes">Each leased file before the run; null when it did not exist.</param>
    /// <param name="Normalized">Each file after Normalize; null until the run normalized them.</param>
    /// <param name="Manifest">The run's manifest, marked recovered after a verified restore.</param>
    public sealed record BackupInfo(
        string Directory,
        Dictionary<string, string?> Hashes,
        Dictionary<string, string?>? Normalized = null,
        string? Manifest = null
    );
}
