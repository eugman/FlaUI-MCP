using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

public sealed class CompositionTests
{
    [Fact]
    public void ArrowsRequireContainedLeftwardGeometry()
    {
        var panel = new CompositionPanel("source", "source", 100, 100, 0, 0, 100, 100);
        var spec = new CompositionSpec(1, "recipe", "crop", [panel], true, Arrows: [new(80, 40, 50)]);
        CaptureComposition.Validate(spec);
        foreach (
            var arrow in new[] { new CompositionArrow(40, 80, 50), new(99, 40, 50), new(80, 40, 2), new(80, -1, 50) }
        )
        {
            Assert.Throws<ArgumentException>(() => CaptureComposition.Validate(spec with { Arrows = [arrow] }));
        }

        Assert.Throws<ArgumentException>(() => CaptureComposition.Validate(spec with { CropOnly = false }));
    }

    [Fact]
    public void OutlineCannotEscapeCrop()
    {
        var panel = new CompositionPanel("source", "source", 10, 10, 0, 0, 5, 5);
        Assert.Throws<ArgumentException>(() =>
            CaptureComposition.Validate(new(1, "recipe", "crop", [panel], true, new(4, 0, 3, 3)))
        );
    }

    [Fact]
    public void CropOnlyRejectsMultiplePanels()
    {
        var panel = new CompositionPanel("source", "source", 10, 10, 0, 0, 5, 5);
        Assert.Throws<ArgumentException>(() =>
            CaptureComposition.Validate(new(1, "recipe", "crop", [panel, panel], true))
        );
    }

    [Fact]
    public void CropOnlyPreservesPixelScaleWithoutLabelsOrMargins()
    {
        var root = Path.Combine(Path.GetTempPath(), "fla_compose_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "source.png");
            using (var bitmap = new Bitmap(10, 10))
            {
                bitmap.SetPixel(2, 3, Color.Red);
                bitmap.Save(source, ImageFormat.Png);
            }

            var manifest = new RunManifest
            {
                Passed = true,
                RequestedScenario = "recipe",
                Screenshots = new() { ["source"] = source },
            };
            var spec = new CompositionSpec(
                1,
                "recipe",
                "crop",
                [new("source", "unused label", 10, 10, 2, 3, 4, 5)],
                true
            );
            var manifestPath = Path.Combine(root, "manifest.json");
            var specPath = Path.Combine(root, "spec.json");
            var output = Path.Combine(root, "crop.png");
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, RunConfig.Json));
            File.WriteAllText(specPath, JsonSerializer.Serialize(spec, RunConfig.Json));
            CaptureComposition.Create(manifestPath, specPath, output);
            var recorded = ArtifactFiles.ReadManifest(manifestPath);
            Assert.Equal(output, recorded.Screenshots["crop"]);
            Assert.Equal("source", Assert.Single(recorded.Compositions["crop"].Panels).Checkpoint);
            Assert.Contains("Derived composition", File.ReadAllText(Path.Combine(root, "index.html")));
            using (var actual = new Bitmap(output))
            {
                Assert.Equal(new Size(4, 5), actual.Size);
                Assert.Equal(Color.Red.ToArgb(), actual.GetPixel(0, 0).ToArgb());
            }

            // Rerunning the same spec is a retry and changes nothing; another crop for the checkpoint is refused.
            CaptureComposition.Create(manifestPath, specPath, output);
            Assert.Equal(recorded.ScreenshotHashes["crop"], ArtifactFiles.Sha256(output));
            File.WriteAllText(
                specPath,
                JsonSerializer.Serialize(
                    spec with
                    {
                        Panels = [new("source", "unused label", 10, 10, 1, 1, 4, 5)],
                    },
                    RunConfig.Json
                )
            );
            Assert.Throws<IOException>(() => CaptureComposition.Create(manifestPath, specPath, output));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void CompositionRejectsACaptureChangedWithoutChangingItsDimensions()
    {
        var root = Path.Combine(Path.GetTempPath(), "fla_compose_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "source.png");
            using (var bitmap = new Bitmap(10, 10))
            {
                bitmap.Save(source, ImageFormat.Png);
            }

            var captured = ArtifactFiles.Sha256(source);
            using (var bitmap = new Bitmap(10, 10))
            {
                bitmap.SetPixel(0, 0, Color.Red);
                bitmap.Save(source, ImageFormat.Png);
            }

            var manifestPath = Path.Combine(root, "manifest.json");
            File.WriteAllText(
                manifestPath,
                JsonSerializer.Serialize(
                    new RunManifest
                    {
                        Passed = true,
                        RequestedScenario = "recipe",
                        Screenshots = new() { ["source"] = source },
                        ScreenshotHashes = new() { ["source"] = captured },
                    },
                    RunConfig.Json
                )
            );
            var specPath = Path.Combine(root, "spec.json");
            File.WriteAllText(
                specPath,
                JsonSerializer.Serialize(
                    new CompositionSpec(1, "recipe", "crop", [new("source", "source", 10, 10, 0, 0, 10, 10)], true),
                    RunConfig.Json
                )
            );

            var output = Path.Combine(root, "crop.png");
            Assert.Throws<IOException>(() => CaptureComposition.Create(manifestPath, specPath, output));
            Assert.False(File.Exists(output));
            Assert.False(ArtifactFiles.ReadManifest(manifestPath).Screenshots.ContainsKey("crop"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
