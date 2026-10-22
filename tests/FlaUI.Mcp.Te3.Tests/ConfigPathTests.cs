using System.Text.Json;
using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>
/// The run config's paths and the artifact containment go through Core's HostPath: a path in the config is a local,
/// fully qualified Windows path once resolved against the config's folder, and an artifact path stays under its root.
/// </summary>
public sealed class ConfigPathTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "fla_config_path_" + Guid.NewGuid().ToString("N"));

    public ConfigPathTests() => Directory.CreateDirectory(root);

    public void Dispose() => Directory.Delete(root, true);

    private RunConfig Read(string te3, string te = "te.exe")
    {
        var path = Path.Combine(root, "config.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new { te3, te }));
        return RunConfig.Read(path);
    }

    [Theory]
    [InlineData(@"\\server\share\te3.exe")]
    [InlineData(@"$HOME\te3.exe")]
    [InlineData("~/te3.exe")]
    [InlineData("/mnt/c/te3.exe")]
    [InlineData(@"%FLA_NOT_DEFINED_9F3%\te3.exe")]
    public void AConfiguredPathCoreRefusesNamesItsField(string te3)
    {
        var error = Assert.Throws<ArgumentException>(() => Read(te3));

        Assert.Contains("te3", error.Message);
    }

    [Fact]
    public void ARelativePathIsResolvedAgainstTheConfigsFolder() =>
        Assert.Equal(Path.Combine(root, "tools", "te.exe"), Read("te3.exe", @"tools\te.exe").Te);

    [Theory]
    [InlineData(".")]
    [InlineData(@"a\..\..\outside")]
    [InlineData(@"..\sibling")]
    public void AnArtifactPathMustLieUnderItsRootNotBeIt(string path) =>
        Assert.Throws<ArgumentException>(() => ArtifactFiles.ContainedPath(path, root));

    [Fact]
    public void AnArtifactPathUnderItsRootIsItsFullPath() =>
        Assert.Equal(Path.Combine(root, "shots", "a.png"), ArtifactFiles.ContainedPath(@"shots\a.png", root));
}
