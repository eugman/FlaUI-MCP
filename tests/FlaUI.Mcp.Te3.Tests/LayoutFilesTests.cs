using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>
/// te3_layout snapshot and restore of layout settings; temporary folders only, never the
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
        File.WriteAllText(
            Path.Combine(settings, "Preferences.json"),
            """{"Mcp":{"Port":42100,"AccessToken":"synthetic-token"},"AiProvider":{"ApiKey":"synthetic-key"}}"""
        );
        File.WriteAllText(Path.Combine(settings, "RecentFiles.json"), "[]");
    }

    public void Dispose() => Directory.Delete(root, true);

    private string Snapshot => Path.Combine(root, "snapshot");

    [Fact]
    public void ASnapshotCopiesOnlyLayoutFilesAndRecordsTheirHashes()
    {
        var result = JsonSerializer.SerializeToElement(LayoutFiles.Snapshot(settings, Snapshot));

        Assert.Equal(2, Directory.GetFiles(Snapshot, "*.json").Count(f => Path.GetFileName(f) != LayoutFiles.Manifest));
        Assert.True(File.Exists(Path.Combine(Snapshot, LayoutFiles.Manifest)));
        Assert.False(File.Exists(Path.Combine(Snapshot, "RecentFiles.json")));
        Assert.False(File.Exists(Path.Combine(Snapshot, "Preferences.json")));
        Assert.DoesNotContain(
            "synthetic-token",
            string.Join("\n", Directory.GetFiles(Snapshot).Select(File.ReadAllText))
        );
        Assert.DoesNotContain(
            "synthetic-key",
            string.Join("\n", Directory.GetFiles(Snapshot).Select(File.ReadAllText))
        );
        // The reply carries each file's hash, as the snapshot's own list does.
        Assert.Equal(
            ArtifactFiles.Sha256(Path.Combine(settings, "Layouts.json")),
            result.GetProperty("files").GetProperty("Layouts.json").GetString()
        );
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
        File.WriteAllText(
            Path.Combine(settings, "Preferences.json"),
            """{"Mcp":{"AccessToken":"new-synthetic-token"}}"""
        );

        var result = JsonSerializer.SerializeToElement(
            LayoutFiles.Restore(Snapshot, settings, te3Running: false, whileRunning: false)
        );

        Assert.Equal("""{"Version":2,"Layouts":[]}""", File.ReadAllText(Path.Combine(settings, "Layouts.json")));
        Assert.Contains("new-synthetic-token", File.ReadAllText(Path.Combine(settings, "Preferences.json")));
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
        File.WriteAllText(Path.Combine(Snapshot, "UiPreferences.json"), "tampered");
        File.WriteAllText(Path.Combine(settings, "Layouts.json"), "current");

        Assert.Throws<InvalidOperationException>(() =>
            LayoutFiles.Restore(Snapshot, settings, te3Running: false, whileRunning: false)
        );
        Assert.Equal("current", File.ReadAllText(Path.Combine(settings, "Layouts.json")));
    }

    [Fact]
    public void RestoreListsTheHashesItVerifiedAndRemovesAFileTheSnapshotDidNotHave()
    {
        // Layouts.json did not exist when the snapshot was taken: putting the snapshot back means removing it.
        File.Delete(Path.Combine(settings, "Layouts.json"));
        LayoutFiles.Snapshot(settings, Snapshot);
        File.WriteAllText(Path.Combine(settings, "Layouts.json"), """{"Version":2,"Layouts":["new"]}""");

        var result = JsonSerializer.SerializeToElement(
            LayoutFiles.Restore(Snapshot, settings, te3Running: false, whileRunning: false)
        );

        Assert.False(File.Exists(Path.Combine(settings, "Layouts.json")));
        Assert.Equal(["Layouts.json"], result.GetProperty("removed").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(
            ArtifactFiles.Sha256(Path.Combine(settings, "UiPreferences.json")),
            result.GetProperty("restored").GetProperty("UiPreferences.json").GetString()
        );
        Assert.False(result.GetProperty("restored").TryGetProperty("Preferences.json", out _));
        Assert.Contains("synthetic-token", File.ReadAllText(Path.Combine(settings, "Preferences.json")));
    }

    [Fact]
    public void RestoreRejectsLegacySnapshotThatListsPreferencesBeforeAnyWrite()
    {
        LayoutFiles.Snapshot(settings, Snapshot);
        var manifest = Path.Combine(Snapshot, LayoutFiles.Manifest);
        var list = JsonNode.Parse(File.ReadAllText(manifest))!.AsObject();
        list["Files"]!["Preferences.json"] = "UNTRUSTED";
        File.WriteAllText(manifest, list.ToJsonString());
        File.WriteAllText(Path.Combine(settings, "Layouts.json"), "current");

        Assert.Throws<InvalidOperationException>(() =>
            LayoutFiles.Restore(Snapshot, settings, te3Running: false, whileRunning: false)
        );
        Assert.Equal("current", File.ReadAllText(Path.Combine(settings, "Layouts.json")));
    }

    private Te3Layout Tool(Te3Interaction interaction, Func<int[]>? running = null) =>
        new(null!, interaction, settings: settings, running: running ?? (() => []), snapshots: root);

    [Fact]
    public async Task SnapshotAndRestoreNeedNoGrantSinceTheyTouchOnlyFiles()
    {
        using var noGrant = new Te3Interaction(() => 0);
        var tool = Tool(noGrant);

        var taken = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { action = "snapshot", path = "snapshot" })
        );
        var restored = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { action = "restore", path = "snapshot" })
        );

        Assert.NotEqual(true, taken.IsError);
        Assert.NotEqual(true, restored.IsError);
        Assert.True(File.Exists(Path.Combine(Snapshot, LayoutFiles.Manifest)));
        // Both are proven by the SHA-256 read-back of each file.
        Assert.Equal(FlaUI.Mcp.Core.Proof.Verified, taken.Outcome?.Proof?.Verdict);
        Assert.Equal(FlaUI.Mcp.Core.Proof.Verified, restored.Outcome?.Proof?.Verdict);
        Assert.Contains("SHA-256", restored.Outcome?.Proof?.Source);
    }

    [Fact]
    public async Task ARestoreWhileTe3RunsIsRefusedBeforeAnyCopy()
    {
        using var noGrant = new Te3Interaction(() => 0);
        var tool = Tool(noGrant, () => [4242]);
        await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new { action = "snapshot", path = "snapshot" }));
        File.WriteAllText(Path.Combine(settings, "Layouts.json"), "changed");

        var refused = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { action = "restore", path = "snapshot" })
        );

        Assert.Equal(true, refused.IsError);
        Assert.Equal("changed", File.ReadAllText(Path.Combine(settings, "Layouts.json")));
    }

    [Theory]
    [InlineData("..\\outside")]
    [InlineData("C:\\Windows\\Temp\\snap")]
    public async Task ASnapshotPathOutsideTheSnapshotRootIsRefused(string path)
    {
        using var noGrant = new Te3Interaction(() => 0);

        var refused = await Tool(noGrant)
            .ExecuteAsync(JsonSerializer.SerializeToElement(new { action = "snapshot", path }));

        Assert.Equal(true, refused.IsError);
        Assert.Contains("a folder name under", refused.Content.Single().Text);
        Assert.False(Directory.Exists(Path.GetFullPath(Path.Combine(root, path))));
    }
}
