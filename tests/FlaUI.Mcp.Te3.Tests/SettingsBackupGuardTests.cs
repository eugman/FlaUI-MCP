using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>
/// What SettingsLease.TryDeleteBackup refuses to delete. Each folder here holds only the leased file names and
/// restore.json, so the guard under test is the only thing between it and deletion.
/// </summary>
public sealed class SettingsBackupGuardTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "fla_backup_guard_" + Guid.NewGuid().ToString("N"));

    public SettingsBackupGuardTests() => Directory.CreateDirectory(root);

    public void Dispose() => Directory.Delete(root, true);

    private string OwnedOnly(string name)
    {
        var folder = Path.Combine(root, name);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "UiPreferences.json"), "{}");
        File.WriteAllText(Path.Combine(folder, "restore.json"), "{}");
        return folder;
    }

    [Fact]
    public void AFolderHoldingOnlyOwnedFilesIsDeleted()
    {
        var backup = OwnedOnly("backup");

        Assert.True(SettingsLease.TryDeleteBackup(backup, OwnedOnly("settings"), OwnedOnly("backups")));

        Assert.False(Directory.Exists(backup));
    }

    [Fact]
    public void TheSettingsFolderIsNeverDeleted()
    {
        var settings = OwnedOnly("settings");

        Assert.False(SettingsLease.TryDeleteBackup(settings + Path.DirectorySeparatorChar, settings, root));

        Assert.True(File.Exists(Path.Combine(settings, "UiPreferences.json")));
        Assert.True(File.Exists(Path.Combine(settings, "restore.json")));
    }

    [Fact]
    public void TheBackupRootIsNeverDeleted()
    {
        var backups = OwnedOnly("backups");

        Assert.False(SettingsLease.TryDeleteBackup(backups, OwnedOnly("settings"), backups));

        Assert.True(File.Exists(Path.Combine(backups, "restore.json")));
    }

    [Fact]
    public void ADriveRootIsProtected()
    {
        var drive = Path.GetPathRoot(root)!;

        Assert.True(SettingsLease.IsProtected(drive, root, root));
        Assert.False(SettingsLease.IsProtected(Path.Combine(root, "backup"), root, Path.Combine(root, "backups")));
    }

    [Fact]
    public void ADeleteThatStopsPartWayNoLongerReadsAsARecoverableBackup()
    {
        var settings = Path.Combine(root, "settings");
        Directory.CreateDirectory(settings);
        File.WriteAllText(Path.Combine(settings, "UiPreferences.json"), "{}");
        var backup = Path.Combine(root, "backup");
        var lease = new SettingsLease(settings, backup);
        lease.Restore();

        using (
            new FileStream(Path.Combine(backup, "UiPreferences.json"), FileMode.Open, FileAccess.Read, FileShare.None)
        )
        {
            Assert.False(lease.DiscardBackup());
        }

        // restore.json went first, so recover does not try to copy from files that may be gone.
        Assert.False(File.Exists(Path.Combine(backup, "restore.json")));
        var again = Assert.Throws<InvalidOperationException>(() => SettingsLease.Recover(backup, false, settings));
        Assert.Contains("already restored and removed", again.Message);
    }
}
