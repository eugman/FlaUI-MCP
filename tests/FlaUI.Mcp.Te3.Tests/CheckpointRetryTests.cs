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

        Assert.Throws<IOException>(() => CaptureAnnotations.Create(manifestPath, specPath));

        Assert.Equal([1, 2, 3], File.ReadAllBytes(output));
        Assert.False(ArtifactFiles.ReadManifest(manifestPath).Screenshots.ContainsKey(spec.Checkpoint));
    }

    [Fact]
    public void AConcurrentWriterOfTheSameManifestIsRefused()
    {
        using (new FileStream(manifestPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Throws<IOException>(() => CaptureAnnotations.Create(manifestPath, specPath));
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
}
