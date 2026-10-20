using System.Text.Json;
using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

public sealed class SettingsAndFixtureTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "fla_settings_fixture_" + Guid.NewGuid().ToString("N")
    );

    public SettingsAndFixtureTests() => Directory.CreateDirectory(root);

    public void Dispose() => Directory.Delete(root, true);

    [Fact]
    public void NormalizationAndRestorePreserveOriginalBytesAndAbsence()
    {
        var preferences = Path.Combine(root, "UiPreferences.json");
        File.WriteAllText(preferences, """{"Skin":"user","MainWindowState":"Normal"}""");
        var original = File.ReadAllBytes(preferences);
        var lease = new SettingsLease(root, Path.Combine(root, "backup"));
        lease.Normalize(true);
        using (var document = JsonDocument.Parse(File.ReadAllText(preferences)))
        {
            Assert.Equal("Maximized", document.RootElement.GetProperty("MainWindowState").GetString());
        }

        File.WriteAllText(Path.Combine(root, "RecentFiles.json"), "new");
        lease.Restore();
        Assert.Equal(original, File.ReadAllBytes(preferences));
        Assert.False(File.Exists(Path.Combine(root, "RecentFiles.json")));
    }

    [Fact]
    public void AcquiredSettingsPersistRecoveryNeedBeforeNormalization()
    {
        File.WriteAllText(Path.Combine(root, "UiPreferences.json"), "{}");
        var manifest = new RunManifest();
        RunManifest? saved = null;
        RunExecutor.AcquireSettings(
            manifest,
            () => saved = JsonSerializer.Deserialize<RunManifest>(JsonSerializer.Serialize(manifest)),
            () => new SettingsLease(root, Path.Combine(root, "backup"))
        );
        Assert.True(saved!.NeedsRecovery);
        Assert.False(saved.SettingsRestored);
        Assert.Equal(Path.Combine(root, "backup"), saved.SettingsBackup);
    }

    [Fact]
    public void TamperedBackupCannotOverwritePreferences()
    {
        File.WriteAllText(Path.Combine(root, "UiPreferences.json"), "{}");
        var lease = new SettingsLease(root, Path.Combine(root, "backup"));
        File.WriteAllText(Path.Combine(root, "backup", "UiPreferences.json"), "tampered");
        var error = Assert.Throws<IOException>(lease.Restore);
        Assert.Contains("checksum mismatch", error.Message);
        Assert.Equal("{}", File.ReadAllText(Path.Combine(root, "UiPreferences.json")));
    }

    [Fact]
    public void RestorePreservesPreExistingRecentFiles()
    {
        File.WriteAllText(Path.Combine(root, "UiPreferences.json"), "{}");
        var recentFiles = Path.Combine(root, "RecentFiles.json");
        File.WriteAllText(recentFiles, "[\"user-model.bim\"]\r\n");
        var original = File.ReadAllBytes(recentFiles);
        var lease = new SettingsLease(root, Path.Combine(root, "backup"));
        lease.Normalize(true);
        File.WriteAllText(recentFiles, "[\"fla_test.bim\"]");

        lease.Restore();

        Assert.Equal(original, File.ReadAllBytes(recentFiles));
    }

    [Fact]
    public void RecoverRefusesSettingsChangedSinceTheRunNormalizedThem()
    {
        // An old backup must not silently overwrite changes the user made after the interrupted run.
        var preferences = Path.Combine(root, "UiPreferences.json");
        File.WriteAllText(preferences, """{"Skin":"user"}""");
        var original = File.ReadAllBytes(preferences);
        var backup = Path.Combine(root, "backup");
        new SettingsLease(root, backup).Normalize();
        File.WriteAllText(preferences, """{"Skin":"changed later"}""");

        var error = Assert.Throws<InvalidOperationException>(() => SettingsLease.Recover(backup, false, root));

        Assert.Contains("UiPreferences.json", error.Message);
        Assert.Contains("--force", error.Message);
        Assert.Equal("""{"Skin":"changed later"}""", File.ReadAllText(preferences));
        SettingsLease.Recover(backup, true, root);
        Assert.Equal(original, File.ReadAllBytes(preferences));
    }

    [Fact]
    public void RecoverRestoresSettingsTheRunLeftNormalized()
    {
        var preferences = Path.Combine(root, "UiPreferences.json");
        File.WriteAllText(preferences, """{"Skin":"user"}""");
        var original = File.ReadAllBytes(preferences);
        var backup = Path.Combine(root, "backup");
        new SettingsLease(root, backup).Normalize(true);

        SettingsLease.Recover(backup, false, root);

        Assert.Equal(original, File.ReadAllBytes(preferences));
        Assert.Empty(Directory.GetFiles(root, "*.tmp"));
    }

    [Fact]
    public void RecoverMarksTheInterruptedRunRecovered()
    {
        File.WriteAllText(Path.Combine(root, "UiPreferences.json"), "{}");
        var backup = Path.Combine(root, "backup");
        var manifestPath = Path.Combine(root, "manifest.json");
        File.WriteAllText(
            manifestPath,
            JsonSerializer.Serialize(
                new RunManifest
                {
                    SettingsBackup = backup,
                    SettingsRestored = false,
                    NeedsRecovery = true,
                },
                RunConfig.Json
            )
        );
        new SettingsLease(root, backup, manifestPath).Normalize();

        Assert.True(SettingsLease.Recover(backup, false, root));

        var manifest = ArtifactFiles.ReadManifest(manifestPath);
        Assert.True(manifest.SettingsRestored);
        Assert.False(manifest.NeedsRecovery);
    }
}
