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
        Assert.Contains("changed after the run normalized them", error.Message);
        Assert.Contains("nothing was restored", error.Message);
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

    [Theory]
    [InlineData(null)]
    [InlineData("[]")]
    [InlineData("not json")]
    public void MissingOrMalformedPreferencesAreRefusedBeforeAnyBackup(string? preferences)
    {
        if (preferences != null)
        {
            File.WriteAllText(Path.Combine(root, "UiPreferences.json"), preferences);
        }

        var backup = Path.Combine(root, "backup");
        var error = Assert.Throws<InvalidOperationException>(() => new SettingsLease(root, backup));

        Assert.Contains("malformed", error.Message);
        Assert.False(Directory.Exists(backup));
    }

    [Fact]
    public void RecoverLeavesAManifestThatNamesAnotherBackupAlone()
    {
        File.WriteAllText(Path.Combine(root, "UiPreferences.json"), "{}");
        var backup = Path.Combine(root, "backup");
        var manifestPath = Path.Combine(root, "manifest.json");
        File.WriteAllText(
            manifestPath,
            JsonSerializer.Serialize(
                new RunManifest { SettingsBackup = Path.Combine(root, "other-backup"), NeedsRecovery = true },
                RunConfig.Json
            )
        );
        new SettingsLease(root, backup, manifestPath).Normalize();

        Assert.False(SettingsLease.Recover(backup, false, root));

        Assert.True(ArtifactFiles.ReadManifest(manifestPath).NeedsRecovery);
    }

    [Fact]
    public void AFileThatCannotBeRestoredDoesNotStopTheOthers()
    {
        var preferences = Path.Combine(root, "UiPreferences.json");
        File.WriteAllText(preferences, """{"Skin":"user"}""");
        var original = File.ReadAllBytes(preferences);
        // Restored first, so the files after it show restoration carries on.
        var blocked = Path.Combine(root, "Preferences.json");
        File.WriteAllText(blocked, "{}");
        var lease = new SettingsLease(root, Path.Combine(root, "backup"));
        lease.Normalize();
        File.Delete(blocked);
        Directory.CreateDirectory(blocked);

        var error = Assert.Throws<AggregateException>(lease.Restore);

        Assert.Contains("Partial restoration", error.Message);
        Assert.Equal(original, File.ReadAllBytes(preferences));
        Assert.Empty(Directory.GetFiles(root, "*.tmp"));
    }

    [Fact]
    public void TheLeaseCoversTe3sOwnSettingsFolder() =>
        Assert.Equal("TabularEditor3", Path.GetFileName(SettingsLease.DefaultDirectory));

    [Fact]
    public void LayoutsAreLeasedAndRestored()
    {
        File.WriteAllText(Path.Combine(root, "UiPreferences.json"), "{}");
        var layouts = Path.Combine(root, "Layouts.json");
        File.WriteAllText(layouts, "user layouts");
        var lease = new SettingsLease(root, Path.Combine(root, "backup"));
        File.WriteAllText(layouts, "run layouts");

        lease.Restore();

        Assert.Equal("user layouts", File.ReadAllText(layouts));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NormalizeSetsTheScreenshotBaseline(bool maximize)
    {
        var preferences = Path.Combine(root, "UiPreferences.json");
        File.WriteAllText(preferences, """{"Skin":"Dark","SkinPalette":"Blue","Language":"de-DE"}""");

        new SettingsLease(root, Path.Combine(root, "backup")).Normalize(maximize);

        using var document = JsonDocument.Parse(File.ReadAllText(preferences));
        var settings = document.RootElement;
        Assert.Equal("Basic", settings.GetProperty("Skin").GetString());
        Assert.Equal("", settings.GetProperty("SkinPalette").GetString());
        Assert.Equal("en-US", settings.GetProperty("Language").GetString());
        Assert.Equal(maximize ? "Maximized" : "Normal", settings.GetProperty("MainWindowState").GetString());
        Assert.True(settings.GetProperty("RememberWindowBounds").GetBoolean());
        Assert.True(settings.GetProperty("RememberWindowState").GetBoolean());
        var bounds = settings.GetProperty("MainWindowRectangle");
        Assert.Equal(
            [40, 40, 1400, 900],
            new[] { "X", "Y", "W", "H" }.Select(side => bounds.GetProperty(side).GetInt32())
        );
    }

    [Fact]
    public void AnExistingBackupIsNeverOverwritten()
    {
        var preferences = Path.Combine(root, "UiPreferences.json");
        File.WriteAllText(preferences, """{"Skin":"user"}""");
        var backup = Path.Combine(root, "backup");
        new SettingsLease(root, backup);
        File.WriteAllText(preferences, """{"Skin":"later"}""");

        Assert.Throws<IOException>(() => new SettingsLease(root, backup));

        Assert.Equal("""{"Skin":"user"}""", File.ReadAllText(Path.Combine(backup, "UiPreferences.json")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecoverWithoutAManifestReportsNothingMarked(bool missingFile)
    {
        File.WriteAllText(Path.Combine(root, "UiPreferences.json"), "{}");
        var backup = Path.Combine(root, "backup");
        var manifest = missingFile ? Path.Combine(root, "gone.json") : null;
        new SettingsLease(root, backup, manifest).Normalize();

        Assert.False(SettingsLease.Recover(backup, false, root));
    }

    [Theory]
    [InlineData("directory")]
    [InlineData("name")]
    public void ABackupThatTargetsOtherFilesIsRefused(string change)
    {
        File.WriteAllText(Path.Combine(root, "UiPreferences.json"), "{}");
        var backup = Path.Combine(root, "backup");
        new SettingsLease(root, backup);
        var infoPath = Path.Combine(backup, "restore.json");
        var info = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(infoPath))!.AsObject();
        if (change == "directory")
        {
            info["Directory"] = Path.Combine(root, "elsewhere");
        }
        else
        {
            var hashes = info["Hashes"]!.AsObject();
            hashes.Remove("Layouts.json");
            hashes["Other.json"] = null;
        }

        File.WriteAllText(infoPath, info.ToJsonString());

        var error = Assert.Throws<InvalidOperationException>(() => SettingsLease.Recover(backup, true, root));
        Assert.Contains("Invalid recovery targets", error.Message);
    }
}
