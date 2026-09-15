using System.Text.Json;
using Xunit;

namespace FlaUI.Automation.Tests;

public sealed class BacklogTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "fla_backlog_test_" + Guid.NewGuid().ToString("N"));
    private readonly string backlog;
    private readonly string docs;

    public BacklogTests()
    {
        Directory.CreateDirectory(root);
        docs = Path.Combine(root, "docs-copy");
        backlog = Path.Combine(root, "screenshot-backlog.json");
        File.WriteAllText(backlog, JsonSerializer.Serialize(new[]
        {
            new { id = "L026", source = "content/assets/images/auto-formatting-settings.png", pages = new[] { "content/references/preferences.md" }, status = "replica-target", recipe = "preferences-auto-formatting" },
            new { id = "L402", source = "content/assets/images/expression-editor.jpg", pages = Array.Empty<string>(), status = "unreviewed", recipe = (string?)null },
            new { id = "R013", source = "https://docs.tabulareditor.com/images/tmdl-options.png", pages = Array.Empty<string>(), status = "source-unavailable", recipe = (string?)null }
        }));
    }

    public void Dispose() => Directory.Delete(root, true);

    private void WriteRun(string checkpoint)
    {
        var png = Path.Combine(root, checkpoint + ".png");
        File.WriteAllBytes(png, [1, 2, 3]);
        var manifest = new RunManifest
        {
            Passed = true, DocsRoot = docs, Output = root,
            Screenshots = new() { [checkpoint] = png }, ScreenshotHashes = new() { [checkpoint] = ArtifactFiles.Sha256(png) }
        };
        File.WriteAllText(Path.Combine(root, "manifest.json"), JsonSerializer.Serialize(manifest, RunConfig.Json));
    }

    [Theory]
    [InlineData("L026", true)]
    [InlineData("R113", true)]
    [InlineData("model-open", false)]
    [InlineData("L26", false)]
    public void OnlyBacklogIdsCountAsItemCheckpoints(string checkpoint, bool expected) => Assert.Equal(expected, Backlog.IsItemId(checkpoint));

    [Fact]
    public async Task PromotesAnItemToItsDocsImagePath()
    {
        WriteRun("L026");
        await ArtifactFiles.PromoteItem(root, "L026", false, backlog);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(docs, "content", "assets", "images", "auto-formatting-settings.png")));
    }

    [Theory]
    [InlineData("L402", "non-PNG")]
    [InlineData("R013", "outside the docs")]
    [InlineData("L999", "Unknown backlog item")]
    public async Task RefusesItemsWithoutADocsPngPath(string id, string message)
    {
        WriteRun(id);
        var error = await Assert.ThrowsAsync<ArgumentException>(() => ArtifactFiles.PromoteItem(root, id, false, backlog));
        Assert.Contains(message, error.Message);
        Assert.False(Directory.Exists(docs));
    }

    [Fact]
    public void FindsTheOriginalOnlyForItemCheckpointsPresentInTheDocsCopy()
    {
        Assert.Null(ArtifactFiles.OriginalFor("L026", docs, backlog));
        var original = Path.Combine(docs, "content", "assets", "images", "auto-formatting-settings.png");
        Directory.CreateDirectory(Path.GetDirectoryName(original)!);
        File.WriteAllBytes(original, [9]);
        Assert.Equal(original, ArtifactFiles.OriginalFor("L026", docs, backlog));
        Assert.Null(ArtifactFiles.OriginalFor("model-open", docs, backlog));
        Assert.Null(ArtifactFiles.OriginalFor("L026", null, backlog));
        Assert.Null(ArtifactFiles.OriginalFor("R013", docs, backlog));
    }
}
