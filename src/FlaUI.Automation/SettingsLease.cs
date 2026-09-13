using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

public sealed class SettingsLease
{
    public static readonly string[] Names = { "Preferences.json", "UiPreferences.json", "Layouts.json", "RecentFiles.json" };
    public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TabularEditor3");
    public static string DefaultBackupRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FlaUI-MCP", "settings-backups");
    public string BackupDirectory { get; }
    public bool Restored { get; private set; }
    private readonly string directory;
    private readonly Dictionary<string, string?> hashes;
    public static void Validate(string? directory = null)
    {
        var path = Path.Combine(directory ?? DefaultDirectory, "UiPreferences.json");
        try { if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject) throw new JsonException("Expected object"); }
        catch (Exception ex) { throw new InvalidOperationException($"TE3 preferences unavailable or malformed: {path}. Open TE3 once, initialize/repair preferences, and close it before testing.", ex); }
    }
    public SettingsLease(string? directory = null, string? backupDirectory = null)
    {
        this.directory = directory ?? DefaultDirectory;
        Validate(this.directory);
        BackupDirectory = backupDirectory ?? Path.Combine(DefaultBackupRoot, "fla_settings_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(BackupDirectory);
        hashes = new();
        foreach (var name in Names)
        {
            var path = Path.Combine(this.directory, name);
            hashes[name] = File.Exists(path) ? Hash(path) : null;
            if (File.Exists(path)) File.Copy(path, Path.Combine(BackupDirectory, name), false);
        }
        File.WriteAllText(Path.Combine(BackupDirectory, "restore.json"), JsonSerializer.Serialize(new BackupInfo(this.directory, hashes)));
    }
    public void Normalize(bool maximize = false)
    {
        var path = Path.Combine(directory, "UiPreferences.json");
        var settings = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        settings["Skin"] = "Basic"; settings["SkinPalette"] = ""; settings["Language"] = "en-US";
        settings["MainWindowState"] = maximize ? "Maximized" : "Normal"; settings["RememberWindowBounds"] = true; settings["RememberWindowState"] = true;
        settings["MainWindowRectangle"] = new JsonObject { ["X"] = 40, ["Y"] = 40, ["W"] = 1400, ["H"] = 900 };
        File.WriteAllText(path, settings.ToJsonString());
    }
    public void Restore() { RestoreBackup(BackupDirectory, directory); Restored = true; }
    public static void RestoreBackup(string backup, string? expectedDirectory = null)
    {
        var info = JsonSerializer.Deserialize<BackupInfo>(File.ReadAllText(Path.Combine(backup, "restore.json")))!;
        if (!Path.GetFullPath(info.Directory).Equals(Path.GetFullPath(expectedDirectory ?? DefaultDirectory), StringComparison.OrdinalIgnoreCase) ||
            info.Hashes.Count != Names.Length || info.Hashes.Keys.Except(Names).Any()) throw new InvalidOperationException("Invalid recovery targets");
        foreach (var (name, hash) in info.Hashes)
            if (hash != null && Hash(Path.Combine(backup, name)) != hash) throw new IOException($"Backup checksum mismatch: {name}");
        var errors = new List<Exception>();
        foreach (var (name, hash) in info.Hashes)
        {
            try
            {
                var target = Path.Combine(info.Directory, name);
                if (hash == null) { if (File.Exists(target)) File.Delete(target); }
                else { File.Copy(Path.Combine(backup, name), target, true); if (Hash(target) != hash) throw new IOException($"Restoration hash mismatch: {name}"); }
            }
            catch (Exception ex) { errors.Add(ex); }
        }
        if (errors.Count != 0) throw new AggregateException("Partial restoration; retain backup and retry recovery", errors);
    }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    public sealed record BackupInfo(string Directory, Dictionary<string, string?> Hashes);
}
