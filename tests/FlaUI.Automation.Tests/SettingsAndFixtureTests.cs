using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace FlaUI.Automation.Tests;
public sealed class SettingsAndFixtureTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "fla_settings_fixture_" + Guid.NewGuid().ToString("N"));
    public SettingsAndFixtureTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);
    [Fact] public void NormalizationAndRestorePreserveOriginalBytesAndAbsence()
    {
        var preferences = Path.Combine(root, "UiPreferences.json");
        File.WriteAllText(preferences, """{"Skin":"user","MainWindowState":"Normal"}""");
        var original = File.ReadAllBytes(preferences);
        var lease = new SettingsLease(root, Path.Combine(root, "backup"));
        lease.Normalize(true);
        using (var document = JsonDocument.Parse(File.ReadAllText(preferences)))
            Assert.Equal("Maximized", document.RootElement.GetProperty("MainWindowState").GetString());
        File.WriteAllText(Path.Combine(root, "RecentFiles.json"), "new");
        lease.Restore();
        Assert.Equal(original, File.ReadAllBytes(preferences));
        Assert.False(File.Exists(Path.Combine(root, "RecentFiles.json")));
    }
    [Fact] public void TamperedBackupCannotOverwritePreferences()
    {
        File.WriteAllText(Path.Combine(root, "UiPreferences.json"), "{}");
        var lease = new SettingsLease(root, Path.Combine(root, "backup"));
        File.WriteAllText(Path.Combine(root, "backup", "UiPreferences.json"), "tampered");
        Assert.ThrowsAny<Exception>(lease.Restore);
        Assert.Equal("{}", File.ReadAllText(Path.Combine(root, "UiPreferences.json")));
    }
}
