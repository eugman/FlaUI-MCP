using System.Text.Json;
using Xunit;

namespace FlaUI.Automation.Tests;
public sealed class OfflineModelOptionsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "fla_offline_options_" + Guid.NewGuid().ToString("N"));
    private readonly string model;
    public OfflineModelOptionsTests()
    {
        Directory.CreateDirectory(root); model = Path.Combine(root, "fixture.bim"); File.WriteAllText(model, "{}");
    }
    [Fact] public void WritesOnlyOfflineFlagInUserSpecificRunLocalFile()
    {
        var path = OfflineModelOptions.Create(model, root, "testuser");
        Assert.Equal("fixture.testuser.tmuo", Path.GetFileName(path));
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        Assert.False(json.RootElement.GetProperty("UseWorkspace").GetBoolean());
        Assert.Single(json.RootElement.EnumerateObject());
        Assert.Equal("{}", File.ReadAllText(model));
    }
    [Fact] public void ExistingUserOptionsAreNotOverwritten()
    {
        var path = OfflineModelOptions.Create(model, root, "testuser"); File.WriteAllText(path, "preserve");
        Assert.Throws<IOException>(() => OfflineModelOptions.Create(model, root, "testuser"));
        Assert.Equal("preserve", File.ReadAllText(path));
    }
    [Fact] public void OutsideRootIsRejected() => Assert.Throws<ArgumentException>(() => OfflineModelOptions.Create(model, Path.Combine(root, "other"), "testuser"));
    [Fact] public void InvalidUsernameIsRejected() => Assert.Throws<ArgumentException>(() => OfflineModelOptions.Create(model, root, "../other"));
    public void Dispose() => Directory.Delete(root, true);
}
