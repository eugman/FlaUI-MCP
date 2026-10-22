using System.Diagnostics;
using System.Text.Json;
using Xunit;

/// <summary>
/// The recover command's checks on a backup of its own: its own runner lock, settings directory and TE3 process list,
/// so the real %TEMP% lock and live TE3 processes are never touched, and each refusal is asserted by its reason.
/// </summary>
public sealed class RecoverCommandTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "fla_recover_test_" + Guid.NewGuid().ToString("N"));

    public RecoverCommandTests() => Directory.CreateDirectory(root);

    public void Dispose() => Directory.Delete(root, true);

    private string LockPath => Path.Combine(root, "runner.lock");

    [Fact]
    public void RecoverRejectsBackupForAnotherSettingsDirectoryBeforeWriting()
    {
        var backup = Path.Combine(root, "backup");
        var target = Path.Combine(root, "other-settings");
        Directory.CreateDirectory(backup);
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "UiPreferences.json"), "{}");
        File.WriteAllText(Path.Combine(backup, "UiPreferences.json"), """{"Skin":"backup"}""");
        File.WriteAllText(
            Path.Combine(backup, "restore.json"),
            JsonSerializer.Serialize(
                new SettingsLease.BackupInfo(target, SettingsLease.Names.ToDictionary(n => n, n => (string?)null))
            )
        );

        // Its own lock, settings directory and an empty TE3 process list: the real lock and live TE3s are never
        // touched, and the refusal is the one for a backup of another settings directory.
        var paths = new RunPaths(LockPath, Path.Combine(root, "settings"));

        var error = Assert.Throws<InvalidOperationException>(() =>
            RunnerCommands.Recover(backup, force: false, paths, running: () => [])
        );

        Assert.Equal("Invalid recovery targets", error.Message);
        Assert.Equal("{}", File.ReadAllText(Path.Combine(target, "UiPreferences.json")));
    }

    [Fact]
    public void RecoverRefusesWhileATe3RunsBeforeReadingTheBackup()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            RunnerCommands.Recover(
                Path.Combine(root, "no-such-backup"),
                force: false,
                new RunPaths(LockPath, Path.Combine(root, "settings")),
                running: () => [4242]
            )
        );

        Assert.Contains("4242", error.Message);
    }
}
