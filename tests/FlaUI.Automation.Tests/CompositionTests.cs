using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using Xunit;

namespace FlaUI.Automation.Tests;

public sealed class CompositionTests
{
    [Fact]
    public void ArrowsRequireContainedLeftwardGeometry()
    {
        var panel = new CompositionPanel("source", "source", 100, 100, 0, 0, 100, 100);
        var spec = new CompositionSpec(1, "recipe", "crop", [panel], true,
            Arrows: [new(80, 40, 50)]);
        CaptureComposition.Validate(spec);
        foreach (var arrow in new[] { new CompositionArrow(40, 80, 50), new(99, 40, 50), new(80, 40, 2), new(80, -1, 50) })
            Assert.Throws<ArgumentException>(() => CaptureComposition.Validate(spec with { Arrows = [arrow] }));
        Assert.Throws<ArgumentException>(() => CaptureComposition.Validate(spec with { CropOnly = false }));
    }
    [Fact]
    public void OutlineCannotEscapeCrop()
    {
        var panel = new CompositionPanel("source", "source", 10, 10, 0, 0, 5, 5);
        Assert.Throws<ArgumentException>(() => CaptureComposition.Validate(new(1, "recipe", "crop", [panel], true, new(4, 0, 3, 3))));
    }

    [Fact]
    public void CropOnlyRejectsMultiplePanels()
    {
        var panel = new CompositionPanel("source", "source", 10, 10, 0, 0, 5, 5);
        Assert.Throws<ArgumentException>(() => CaptureComposition.Validate(new(1, "recipe", "crop", [panel, panel], true)));
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
                Passed = true, RequestedScenario = "recipe", Screenshots = new() { ["source"] = source }
            };
            var spec = new CompositionSpec(1, "recipe", "crop", [new("source", "unused label", 10, 10, 2, 3, 4, 5)], true);
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
            Assert.Throws<IOException>(() => CaptureComposition.Create(manifestPath, specPath, output));
        }
        finally { Directory.Delete(root, true); }
    }
}
