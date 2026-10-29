using System.Text.Json;

/// <summary>
/// The settings files that carry TE3's layout across launches (observed 3.27.1): TE3 rewrites Layouts.json
/// and UiPreferences.json when it closes, so a preset or a crash in one run carries into the next.
/// Preferences.json also contains connection and provider credentials and must never enter a layout snapshot.
/// A snapshot copies only Layouts.json and UiPreferences.json with their hashes; a restore checks those files are
/// unchanged, copies them back and reads them back. Preferences.json is excluded from both operations.
/// </summary>
internal static class LayoutFiles
{
    public const string Manifest = "layout-snapshot.json";

    public static readonly string[] Names = ["Layouts.json", "UiPreferences.json"];

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

        // Validate every source before creating or filling the snapshot. A linked layout file must
        // not make a copy of arbitrary settings or another file outside TE3's settings folder.
        var sources = Names
            .Where(n => File.Exists(Path.Combine(settings, n)))
            .ToDictionary(n => n, n => LayoutPath(settings, n));
        Directory.CreateDirectory(target);
        var hashes = new Dictionary<string, string>();
        foreach (var (name, source) in sources)
        {
            var destination = LayoutPath(target, name);
            File.Copy(source, destination);
            hashes[name] = ArtifactFiles.Sha256(destination);
            if (hashes[name] != ArtifactFiles.Sha256(source))
            {
                throw new IOException($"{name} changed while it was copied; take the snapshot again with TE3 closed.");
            }
        }

        var list = new SnapshotList(hashes, [.. Names.Except(hashes.Keys)]);
        File.WriteAllText(LayoutPath(target, Manifest), JsonSerializer.Serialize(list));
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
        // Check all source and destination paths before the first copy or delete. Hash validation
        // alone is insufficient: a listed file with the right bytes may still link outside here.
        var sources = list.Files.Keys.ToDictionary(n => n, n => LayoutPath(snapshot, n));
        var destinations = Names.ToDictionary(n => n, n => LayoutPath(settings, n));
        var changed = list.Files.Where(h => ArtifactFiles.Sha256(sources[h.Key]) != h.Value).ToList();
        if (changed.Count > 0)
        {
            throw new InvalidOperationException(
                $"The snapshot changed since it was taken ({string.Join(", ", changed.Select(c => c.Key))}); nothing was copied."
            );
        }

        foreach (var name in list.Files.Keys)
        {
            var temporary = Path.Combine(settings, name + "." + Guid.NewGuid().ToString("N") + ".tmp");
            File.Copy(sources[name], temporary);
            File.Move(temporary, destinations[name], overwrite: true);
        }

        var removed = list.Missing.Where(n => File.Exists(destinations[n])).ToArray();
        foreach (var name in removed)
        {
            File.Delete(destinations[name]);
        }

        var unverified = list
            .Files.Where(h => ArtifactFiles.Sha256(destinations[h.Key]) != h.Value)
            .Select(h => h.Key)
            .Concat(list.Missing.Where(n => File.Exists(destinations[n])))
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
        names.ToDictionary(n => n, n => ArtifactFiles.Sha256(LayoutPath(folder, n)));

    private static string LayoutPath(string folder, string name) => ArtifactFiles.ContainedPath(name, folder);

    private static SnapshotList ReadList(string snapshot)
    {
        var list = JsonSerializer.Deserialize<SnapshotList>(File.ReadAllText(LayoutPath(snapshot, Manifest)));
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
