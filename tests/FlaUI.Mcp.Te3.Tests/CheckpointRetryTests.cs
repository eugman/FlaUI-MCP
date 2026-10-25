using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>Annotate and compose write a PNG and then the manifest; a crash between them must be retryable.</summary>
public sealed class CheckpointRetryTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "fla_checkpoint_test_" + Guid.NewGuid().ToString("N")
    );
    private readonly string manifestPath;
    private readonly string specPath;
    private readonly string output;
    private readonly AnnotationSpec spec = new(1, "backlog-dialogs", "L026", 100, 80, Boxes: [new(5, 5, 20, 10)]);

    public CheckpointRetryTests()
    {
        Directory.CreateDirectory(root);
        var clean = Path.Combine(root, "L026.png");
        using (var bitmap = new Bitmap(100, 80))
        {
            bitmap.Save(clean, ImageFormat.Png);
        }

        manifestPath = Path.Combine(root, "manifest.json");
        File.WriteAllText(
            manifestPath,
            JsonSerializer.Serialize(
                new RunManifest
                {
                    Passed = true,
                    RequestedScenario = "backlog-dialogs",
                    Output = root,
                    Screenshots = new() { ["L026"] = clean },
                    ScreenshotHashes = new() { ["L026"] = ArtifactFiles.Sha256(clean) },
                },
                RunConfig.Json
            )
        );
        specPath = Path.Combine(root, "L026.annotations.json");
        File.WriteAllText(specPath, JsonSerializer.Serialize(spec, RunConfig.Json));
        output = Path.Combine(root, spec.Checkpoint + ".png");
    }

    public void Dispose() => Directory.Delete(root, true);

    private void AssertRecorded()
    {
        var manifest = ArtifactFiles.ReadManifest(manifestPath);
        Assert.Equal(output, manifest.Screenshots[spec.Checkpoint]);
        Assert.Equal(ArtifactFiles.Sha256(output), manifest.ScreenshotHashes[spec.Checkpoint]);
        Assert.Empty(Directory.GetFiles(root, "*.partial"));
    }

    [Fact]
    public void RetryAfterACrashBeforeTheManifestUpdateAcceptsTheIdenticalPng()
    {
        var before = File.ReadAllText(manifestPath);
        CaptureAnnotations.Create(manifestPath, specPath);
        // The PNG was written but the manifest never recorded it.
        File.WriteAllText(manifestPath, before);

        CaptureAnnotations.Create(manifestPath, specPath);

        AssertRecorded();
    }

    [Fact]
    public void RetryAfterACrashBeforeTheRenameRestoresThePng()
    {
        CaptureAnnotations.Create(manifestPath, specPath);
        File.Delete(output);

        CaptureAnnotations.Create(manifestPath, specPath);

        AssertRecorded();
    }

    [Fact]
    public void AnExistingPngWithOtherContentIsNeverReplaced()
    {
        File.WriteAllBytes(output, [1, 2, 3]);

        var error = Assert.Throws<IOException>(() => CaptureAnnotations.Create(manifestPath, specPath));

        Assert.Contains("already exists with other content", error.Message);

        Assert.Equal([1, 2, 3], File.ReadAllBytes(output));
        Assert.False(ArtifactFiles.ReadManifest(manifestPath).Screenshots.ContainsKey(spec.Checkpoint));
    }

    [Fact]
    public void AConcurrentWriterOfTheSameManifestIsRefused()
    {
        using (new FileStream(manifestPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            var error = Assert.Throws<IOException>(() => CaptureAnnotations.Create(manifestPath, specPath));
            Assert.Contains("retry when it finishes", error.Message);
        }

        Assert.False(File.Exists(output));
    }

    [Fact]
    public void FieldsFromANewerRunnerSurviveTheManifestRewrite()
    {
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        manifest["futureField"] = new JsonObject { ["kept"] = true };
        File.WriteAllText(manifestPath, manifest.ToJsonString());

        CaptureAnnotations.Create(manifestPath, specPath);

        var rewritten = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        Assert.True(rewritten["futureField"]!["kept"]!.GetValue<bool>());
    }

    [Fact]
    public void AManifestWithoutASchemaVersionIsRejected()
    {
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        manifest.Remove("schemaVersion");
        File.WriteAllText(manifestPath, manifest.ToJsonString());

        Assert.Throws<InvalidDataException>(() => ArtifactFiles.ReadManifest(manifestPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARetryFindingAnotherEntryForTheCheckpointIsRefused(bool samePath)
    {
        // Same checkpoint, but another path or other pixels: not this command's interrupted attempt.
        var manifest = ArtifactFiles.ReadManifest(manifestPath);
        manifest.Screenshots[spec.Checkpoint] = samePath ? output : Path.Combine(root, "other.png");
        manifest.ScreenshotHashes[spec.Checkpoint] = "00";
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, RunConfig.Json));
        var before = File.ReadAllText(manifestPath);

        var error = Assert.Throws<IOException>(() => CaptureAnnotations.Create(manifestPath, specPath));

        Assert.Contains(spec.Checkpoint, error.Message);
        Assert.False(File.Exists(output));
        Assert.Equal(before, File.ReadAllText(manifestPath));
        Assert.Empty(Directory.GetFiles(root, "*.partial"));
    }

    [Fact]
    public void APartialFileLeftByACrashDoesNotBlockTheRetry()
    {
        File.WriteAllBytes(output + ".partial", [1, 2, 3]);

        CaptureAnnotations.Create(manifestPath, specPath);

        AssertRecorded();
    }

    [Fact]
    public void AMissingRunFolderIsNotReportedAsAnotherWriter()
    {
        var missing = Path.Combine(root, "no-such-run", "manifest.json");

        var error = Assert.Throws<DirectoryNotFoundException>(() => CheckpointOutput.Lock(missing));

        Assert.DoesNotContain("Another command", error.Message);
    }

    [Fact]
    public void CheckpointExistsWithItsRecordedBytesBeforeManifestIsPublished()
    {
        var manifest = ArtifactFiles.ReadManifest(manifestPath);
        var recorded = false;

        CheckpointOutput.Publish(
            manifestPath,
            manifest,
            "derived",
            Path.Combine(root, "derived.png"),
            stream => stream.Write([1, 2, 3]),
            () =>
            {
                recorded = true;
                Assert.True(File.Exists(Path.Combine(root, "derived.png")));
                Assert.False(File.Exists(Path.Combine(root, "derived.png.partial")));
                Assert.Equal(
                    ArtifactFiles.Sha256(Path.Combine(root, "derived.png")),
                    manifest.ScreenshotHashes["derived"]
                );
            }
        );

        Assert.True(recorded);
        Assert.Equal(
            ArtifactFiles.Sha256(Path.Combine(root, "derived.png")),
            ArtifactFiles.ReadManifest(manifestPath).ScreenshotHashes["derived"]
        );
    }
}
