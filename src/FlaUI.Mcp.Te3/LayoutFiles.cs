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

    /// <summary>Copies the files into <paramref name="target"/>, which must not exist yet.</summary>
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

        File.WriteAllText(Path.Combine(target, Manifest), JsonSerializer.Serialize(hashes));
        return new
        {
            snapshot = target,
            files = hashes.Keys.ToArray(),
            missing = Names.Except(hashes.Keys).ToArray(),
        };
    }

    /// <summary>
    /// Copies a snapshot back. Refused while TE3 runs (its close would overwrite the restore) unless
    /// <paramref name="whileRunning"/>, and refused before any copy when a snapshot file no longer has its hash.
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

        var hashes =
            JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(snapshot, Manifest)))
            ?? throw new InvalidOperationException("The snapshot has no file list; nothing was copied.");
        var changed = hashes.Where(h => ArtifactFiles.Sha256(Path.Combine(snapshot, h.Key)) != h.Value).ToList();
        if (changed.Count > 0 || hashes.Keys.Any(k => !Names.Contains(k)))
        {
            throw new InvalidOperationException(
                "The snapshot changed since it was taken ("
                    + string.Join(", ", changed.Select(c => c.Key))
                    + "); nothing was copied."
            );
        }

        foreach (var (name, _) in hashes)
        {
            var temporary = Path.Combine(settings, name + "." + Guid.NewGuid().ToString("N") + ".tmp");
            File.Copy(Path.Combine(snapshot, name), temporary);
            File.Move(temporary, Path.Combine(settings, name), overwrite: true);
        }

        var verified = hashes.All(h => ArtifactFiles.Sha256(Path.Combine(settings, h.Key)) == h.Value);
        return new { restored = hashes.Keys.ToArray(), verified };
    }
}
