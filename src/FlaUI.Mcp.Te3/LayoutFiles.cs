using System.Text.Json;

/// <summary>
/// The settings files that carry TE3's layout across launches (observed 3.27.1): TE3 rewrites Layouts.json,
/// UiPreferences.json and Preferences.json when it closes, so a preset or a crash in one run carries into the next.
/// A snapshot copies them with their hashes; a restore checks the snapshot is unchanged, copies it back and reads it
/// back.
/// </summary>
internal static class LayoutFiles
{
    public const string Manifest = "layout-snapshot.json";

    public static readonly string[] Names = ["Layouts.json", "UiPreferences.json", "Preferences.json"];

    /// <summary>Where snapshots go unless the host is told otherwise: under the host's own folder, never elsewhere.</summary>
    public static string DefaultRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FlaUI.Mcp.Te3",
            "layout-snapshots"
        );

    /// <summary>
    /// te3_layout snapshot or restore with <paramref name="path"/> confined under <paramref name="root"/>: a snapshot
    /// never lands, and a restore never reads, outside the host's snapshot folder.
    /// </summary>
    public static object Run(
        bool snapshot,
        string path,
        string root,
        string settings,
        Func<bool> te3Running,
        bool whileRunning
    )
    {
        var named =
            $"path is a folder name under {root}, not a full path or one that leaves it ('{path}'); nothing was copied.";
        if (Path.IsPathRooted(path))
        {
            throw new ArgumentException(named);
        }

        Directory.CreateDirectory(root);
        string target;
        try
        {
            target = ArtifactFiles.ContainedPath(path, root);
        }
        catch (ArgumentException error)
        {
            throw new ArgumentException(named, error);
        }

        return snapshot ? Snapshot(settings, target) : Restore(target, settings, te3Running(), whileRunning);
    }

    private sealed record SnapshotList(Dictionary<string, string> Files, string[] Missing);

    /// <summary>Copies the files into <paramref name="target"/>, which must not exist yet; the reply lists each hash.</summary>
    public static object Snapshot(string settings, string target)
    {
        if (Directory.Exists(target) || File.Exists(target))
        {
            throw new IOException($"Snapshot folder {target} already exists; pick a new one. Nothing was copied.");
        }

        Directory.CreateDirectory(target);
        var hashes = new Dictionary<string, string>();
        foreach (var name in Names.Where(n => File.Exists(Path.Combine(settings, n))))
        {
            File.Copy(Path.Combine(settings, name), Path.Combine(target, name));
            hashes[name] = ArtifactFiles.Sha256(Path.Combine(target, name));
            if (hashes[name] != ArtifactFiles.Sha256(Path.Combine(settings, name)))
            {
                throw new IOException($"{name} changed while it was copied; take the snapshot again with TE3 closed.");
            }
        }

        var list = new SnapshotList(hashes, [.. Names.Except(hashes.Keys)]);
        File.WriteAllText(Path.Combine(target, Manifest), JsonSerializer.Serialize(list));
        return new
        {
            snapshot = target,
            files = list.Files,
            missing = list.Missing,
            readBack = Hashes(settings, list.Files.Keys),
        };
    }

    /// <summary>
    /// Copies a snapshot back and removes the files it did not have. Refused while TE3 runs (its close would overwrite
    /// the restore) unless <paramref name="whileRunning"/>, and refused before any copy when a snapshot file no longer
    /// has its hash. A restore whose read-back differs is a failure.
    /// </summary>
    public static object Restore(string snapshot, string settings, bool te3Running, bool whileRunning)
    {
        if (te3Running && !whileRunning)
        {
            throw new InvalidOperationException(
                "TE3 is running, and it rewrites these files when it closes, so this restore would be lost. Close TE3 "
                    + "first, or pass whileRunning: true if the user asked for it. Nothing was copied."
            );
        }

        var list = ReadList(snapshot);
        var changed = list.Files.Where(h => ArtifactFiles.Sha256(Path.Combine(snapshot, h.Key)) != h.Value).ToList();
        if (changed.Count > 0)
        {
            throw new InvalidOperationException(
                $"The snapshot changed since it was taken ({string.Join(", ", changed.Select(c => c.Key))}); nothing was copied."
            );
        }

        foreach (var name in list.Files.Keys)
        {
            var temporary = Path.Combine(settings, name + "." + Guid.NewGuid().ToString("N") + ".tmp");
            File.Copy(Path.Combine(snapshot, name), temporary);
            File.Move(temporary, Path.Combine(settings, name), overwrite: true);
        }

        var removed = list.Missing.Where(n => File.Exists(Path.Combine(settings, n))).ToArray();
        foreach (var name in removed)
        {
            File.Delete(Path.Combine(settings, name));
        }

        var unverified = list
            .Files.Where(h => ArtifactFiles.Sha256(Path.Combine(settings, h.Key)) != h.Value)
            .Select(h => h.Key)
            .Concat(list.Missing.Where(n => File.Exists(Path.Combine(settings, n))))
            .ToList();
        if (unverified.Count > 0)
        {
            throw new IOException(
                $"The restore did not read back for {string.Join(", ", unverified)}; inspect the settings folder."
            );
        }

        return new
        {
            restored = list.Files,
            removed,
            verified = true,
            readBack = Hashes(settings, list.Files.Keys),
        };
    }

    // The SHA-256 of each file as it reads now, for the reply's proof.
    private static Dictionary<string, string> Hashes(string folder, IEnumerable<string> names) =>
        names.ToDictionary(n => n, n => ArtifactFiles.Sha256(Path.Combine(folder, n)));

    private static SnapshotList ReadList(string snapshot)
    {
        var list = JsonSerializer.Deserialize<SnapshotList>(File.ReadAllText(Path.Combine(snapshot, Manifest)));
        if (
            list?.Files == null
            || list.Missing == null
            || list.Files.Keys.Concat(list.Missing).Any(k => !Names.Contains(k))
        )
        {
            throw new InvalidOperationException(
                "The snapshot's file list is missing or names other files; nothing was copied."
            );
        }

        return list;
    }
}
