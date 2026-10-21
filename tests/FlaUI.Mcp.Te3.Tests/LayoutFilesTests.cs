using System.Text.Json;
using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>
/// te3_layout snapshot and restore of the three settings files TE3 rewrites on close; temporary folders only, never the
/// user's %LocalAppData%\TabularEditor3.
/// </summary>
public sealed class LayoutFilesTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "fla_layoutfiles_" + Guid.NewGuid().ToString("N"));
    private readonly string settings;

    public LayoutFilesTests()
    {
        settings = Path.Combine(root, "TabularEditor3");
        Directory.CreateDirectory(settings);
        File.WriteAllText(Path.Combine(settings, "Layouts.json"), """{"Version":2,"Layouts":[]}""");
        File.WriteAllText(Path.Combine(settings, "UiPreferences.json"), """{"MainWindowState":"Normal"}""");
        File.WriteAllText(Path.Combine(settings, "Preferences.json"), """{"Mcp":{"Port":42100}}""");
        File.WriteAllText(Path.Combine(settings, "RecentFiles.json"), "[]");
    }

    public void Dispose() => Directory.Delete(root, true);

    private string Snapshot => Path.Combine(root, "snapshot");

    [Fact]
    public void ASnapshotCopiesTheThreeFilesAndRecordsTheirHashes()
    {
        var result = JsonSerializer.SerializeToElement(LayoutFiles.Snapshot(settings, Snapshot));

        Assert.Equal(3, Directory.GetFiles(Snapshot, "*.json").Count(f => Path.GetFileName(f) != LayoutFiles.Manifest));
        Assert.True(File.Exists(Path.Combine(Snapshot, LayoutFiles.Manifest)));
        Assert.False(File.Exists(Path.Combine(Snapshot, "RecentFiles.json")));
        Assert.Equal(3, result.GetProperty("files").GetArrayLength());
    }

    [Fact]
    public void ASnapshotNeverOverwritesAnEarlierOne()
    {
        LayoutFiles.Snapshot(settings, Snapshot);

        Assert.Throws<IOException>(() => LayoutFiles.Snapshot(settings, Snapshot));
    }

    [Fact]
    public void RestorePutsTheSnapshotBackAndReadsItBack()
    {
        LayoutFiles.Snapshot(settings, Snapshot);
        File.WriteAllText(Path.Combine(settings, "Layouts.json"), """{"Version":2,"Layouts":["preset"]}""");

        var result = JsonSerializer.SerializeToElement(
            LayoutFiles.Restore(Snapshot, settings, te3Running: false, whileRunning: false)
        );

        Assert.Equal("""{"Version":2,"Layouts":[]}""", File.ReadAllText(Path.Combine(settings, "Layouts.json")));
        Assert.True(result.GetProperty("verified").GetBoolean());
    }

    [Fact]
    public void RestoreIsRefusedWhileTe3RunsUnlessTheUserAsked()
    {
        // TE3 rewrites these files when it closes, so a restore under a running TE3 is lost.
        LayoutFiles.Snapshot(settings, Snapshot);
        File.WriteAllText(Path.Combine(settings, "Layouts.json"), "changed");

        var error = Assert.Throws<InvalidOperationException>(() =>
            LayoutFiles.Restore(Snapshot, settings, te3Running: true, whileRunning: false)
        );

        Assert.Contains("rewrites", error.Message);
        Assert.Equal("changed", File.ReadAllText(Path.Combine(settings, "Layouts.json")));
        LayoutFiles.Restore(Snapshot, settings, te3Running: true, whileRunning: true);
    }

    [Fact]
    public void ASnapshotChangedSinceItWasTakenIsNotRestored()
    {
        LayoutFiles.Snapshot(settings, Snapshot);
        File.WriteAllText(Path.Combine(Snapshot, "Preferences.json"), "tampered");
        File.WriteAllText(Path.Combine(settings, "Layouts.json"), "current");

        Assert.Throws<InvalidOperationException>(() =>
            LayoutFiles.Restore(Snapshot, settings, te3Running: false, whileRunning: false)
        );
        Assert.Equal("current", File.ReadAllText(Path.Combine(settings, "Layouts.json")));
    }

    [Fact]
    public async Task TheLayoutToolSnapshotsAndRefusesARestoreWhileTe3RunsBeforeAnyInput()
    {
        using var interaction = new Te3Interaction(() => 0);
        interaction.Begin("arrange TE3", userApproved: true);
        var tool = new Te3Layout(null!, interaction, settings: settings, running: () => [4242]);

        var taken = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { action = "snapshot", path = Snapshot })
        );
        var refused = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { action = "restore", path = Snapshot })
        );

        Assert.NotEqual(true, taken.IsError);
        Assert.True(File.Exists(Path.Combine(Snapshot, LayoutFiles.Manifest)));
        Assert.Equal(true, refused.IsError);
        Assert.Equal(0, refused.Outcome?.InputsSent);
    }
}
