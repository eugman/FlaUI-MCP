using System.Text.Json;
using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

public sealed class BacklogTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "fla_backlog_test_" + Guid.NewGuid().ToString("N"));
    private readonly string backlog;
    private readonly string docs;
    private readonly string redactedBacklog;

    public BacklogTests()
    {
        Directory.CreateDirectory(root);
        docs = Path.Combine(root, "docs-copy");
        backlog = Path.Combine(root, "screenshot-backlog.json");
        redactedBacklog = Path.Combine(root, "redacted-backlog.json");
        File.WriteAllText(
            backlog,
            JsonSerializer.Serialize(
                new object[]
                {
                    new
                    {
                        id = "L026",
                        source = "content/assets/images/auto-formatting-settings.png",
                        pages = new[] { "content/references/preferences.md" },
                        status = "replica-target",
                        recipe = "preferences-auto-formatting",
                    },
                    new
                    {
                        id = "L402",
                        source = "content/assets/images/expression-editor.jpg",
                        pages = Array.Empty<string>(),
                        status = "unreviewed",
                        recipe = (string?)null,
                    },
                    new
                    {
                        id = "R013",
                        source = "https://docs.tabulareditor.com/images/tmdl-options.png",
                        pages = Array.Empty<string>(),
                        status = "source-unavailable",
                        recipe = (string?)null,
                    },
                }
            )
        );
        File.WriteAllText(
            redactedBacklog,
            JsonSerializer.Serialize(
                new object[]
                {
                    new
                    {
                        id = "L247",
                        source = "content/assets/images/about-te3.png",
                        pages = Array.Empty<string>(),
                        status = "replica-target",
                        recipe = (string?)null,
                        requiresRedaction = true,
                    },
                }
            )
        );
    }

    public void Dispose() => Directory.Delete(root, true);

    private void WriteRun(string checkpoint)
    {
        var png = Path.Combine(root, checkpoint + ".png");
        File.WriteAllBytes(png, [1, 2, 3]);
        var manifest = new RunManifest
        {
            Passed = true,
            DocsRoot = docs,
            Output = root,
            Screenshots = new() { [checkpoint] = png },
            ScreenshotHashes = new() { [checkpoint] = ArtifactFiles.Sha256(png) },
        };
        File.WriteAllText(Path.Combine(root, "manifest.json"), JsonSerializer.Serialize(manifest, RunConfig.Json));
    }

    [Theory]
    [InlineData("L026", true)]
    [InlineData("R113", true)]
    [InlineData("model-open", false)]
    [InlineData("L26", false)]
    public void OnlyBacklogIdsCountAsItemCheckpoints(string checkpoint, bool expected) =>
        Assert.Equal(expected, Backlog.IsItemId(checkpoint));

    [Fact]
    public async Task PromotesAnItemToItsDocsImagePath()
    {
        WriteRun("L026");
        await ArtifactFiles.PromoteItem(root, "L026", false, backlog);
        Assert.Equal(
            [1, 2, 3],
            File.ReadAllBytes(Path.Combine(docs, "content", "assets", "images", "auto-formatting-settings.png"))
        );
    }

    [Theory]
    [InlineData("L402", "non-PNG")]
    [InlineData("R013", "outside the docs")]
    [InlineData("L999", "Unknown backlog item")]
    public async Task RefusesItemsWithoutADocsPngPath(string id, string message)
    {
        WriteRun(id);
        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            ArtifactFiles.PromoteItem(root, id, false, backlog)
        );
        Assert.Contains(message, error.Message);
        Assert.False(Directory.Exists(docs));
    }

    [Fact]
    public async Task TheRawPromotePathCannotPublishAnUnredactedItem()
    {
        // The guard lived only in PromoteItem, but Promote is public and the supported command
        // line reaches it as `promote <run> L247 <docs path>`, which published the clean About
        // capture — licence holder, installations and session id — reporting success.
        WriteRun("L247");
        var destination = Path.Combine(docs, "content", "assets", "images", "about-te3.png");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ArtifactFiles.Promote(root, "L247", destination, false, redactedBacklog)
        );
        Assert.Contains("requiresRedaction", error.Message);
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public void ACheckpointThatIsNotABacklogItemNeedsNoRedaction() =>
        Assert.False(ArtifactFiles.RequiresRedaction("model-open", redactedBacklog));

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

    [Fact]
    public void OriginalImageCannotEscapeTheDocsCopy()
    {
        var outside = Path.Combine(root, "outside.png");
        File.WriteAllBytes(outside, [1, 2, 3]);
        var traversalBacklog = Path.Combine(root, "traversal-backlog.json");
        File.WriteAllText(
            traversalBacklog,
            """[{"id":"L026","source":"content/../../outside.png","pages":[],"status":"unreviewed"}]"""
        );

        Assert.Null(ArtifactFiles.OriginalFor("L026", docs, traversalBacklog));
    }

    [Fact]
    public async Task ExistingUntrackedAnnotationSpecCannotBeOverwritten()
    {
        WriteRun("L026");
        var annotated = Path.Combine(root, "L026-annotated.png");
        File.WriteAllBytes(annotated, [4, 5, 6]);
        var manifestPath = Path.Combine(root, "manifest.json");
        var manifest = ArtifactFiles.ReadManifest(manifestPath);
        manifest.Screenshots["L026-annotated"] = annotated;
        manifest.ScreenshotHashes["L026-annotated"] = ArtifactFiles.Sha256(annotated);
        manifest.Annotations["L026-annotated"] = new AnnotationSpec(
            1,
            "recipe",
            "L026",
            10,
            10,
            Boxes: [new(1, 1, 3, 3)]
        );
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, RunConfig.Json));

        var image = Path.Combine(docs, "content", "assets", "images", "auto-formatting-settings.png");
        var spec = Path.ChangeExtension(image, ".annotations.json");
        Directory.CreateDirectory(Path.GetDirectoryName(spec)!);
        File.WriteAllText(spec, "keep this local edit");

        await Assert.ThrowsAsync<IOException>(() => ArtifactFiles.PromoteItem(root, "L026", true, backlog));
        Assert.Equal("keep this local edit", File.ReadAllText(spec));
        Assert.False(File.Exists(image));
    }

    [Fact]
    public void TheBacklogPathCanBeConfigured()
    {
        // A worktree has no local/ of its own; the backlog lives in the main checkout.
        Assert.Equal(backlog, Backlog.DefaultPath(backlog, root));
        var missing = Assert.Throws<FileNotFoundException>(() =>
            Backlog.DefaultPath(Path.Combine(root, "absent.json"), root)
        );
        Assert.Contains(Backlog.EnvironmentVariable, missing.Message);
    }

    [Fact]
    public void TheSearchStopsAtAWorktreeRoot()
    {
        // In a worktree .git is a file; the search must not climb past it into an unrelated local/.
        var worktree = Path.Combine(root, "worktree");
        var bin = Path.Combine(worktree, "bin");
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(worktree, ".git"), "gitdir: ../main/.git/worktrees/x");
        Directory.CreateDirectory(Path.Combine(root, "local"));
        File.Copy(backlog, Path.Combine(root, "local", "screenshot-backlog.json"));

        Assert.Throws<FileNotFoundException>(() => Backlog.DefaultPath(null, bin));
    }
}
