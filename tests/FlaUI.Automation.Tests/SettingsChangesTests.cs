using Xunit;

namespace FlaUI.Automation.Tests;

public sealed class SettingsChangesTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "fla_settings_changes_" + Guid.NewGuid().ToString("N"));
    public SettingsChangesTests() => Directory.CreateDirectory(Path.Combine(root, "AI", "Skills"));
    public void Dispose() => Directory.Delete(root, true);

    private void Write(string relative, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root, relative))!);
        File.WriteAllText(Path.Combine(root, relative), text);
    }

    [Fact]
    public void ReportsTopLevelAndAiChangesButIgnoresCachesAndTheLog()
    {
        Write("Preferences.json", "{}");
        Write("kb.sqlite", "a");
        Write("applicationlog.txt", "start");
        Write(Path.Combine("AI", "Skills", "skill.md"), "one");
        Write(Path.Combine("WebView2", "Cache", "data_0"), "x");
        var before = SettingsChanges.Snapshot(root);

        Write("kb.sqlite", "b");
        Write("Layouts.json", "{}");
        File.Delete(Path.Combine(root, "AI", "Skills", "skill.md"));
        Write("applicationlog.txt", "start\nmore");
        Write(Path.Combine("WebView2", "Cache", "data_0"), "y");

        Assert.Equal(
            // Sorted by file name.
            ["removed: " + Path.Combine("AI", "Skills", "skill.md"), "changed: kb.sqlite", "added: Layouts.json"],
            SettingsChanges.Compare(before, SettingsChanges.Snapshot(root)));
    }

    [Fact]
    public void UnchangedSettingsReportNothing()
    {
        Write("Preferences.json", "{}");
        Assert.Empty(SettingsChanges.Compare(SettingsChanges.Snapshot(root), SettingsChanges.Snapshot(root)));
    }
}
