using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using Xunit;

namespace FlaUI.Automation.Tests;

public sealed class AnnotationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "fla_annotation_test_" + Guid.NewGuid().ToString("N"));
    public AnnotationTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);

    private static AnnotationSpec Spec(AnnotationRect? crop = null, AnnotationRect[]? boxes = null, AnnotationArrow[]? arrows = null,
        AnnotationRect[]? redactions = null) => new(1, "backlog-dialogs", "L026", 100, 80, crop, boxes, arrows, redactions);

    [Fact]
    public void ValidationKeepsShapesInsideTheOutput()
    {
        CaptureAnnotations.Validate(Spec(crop: new(10, 10, 60, 50), boxes: [new(0, 0, 60, 50)]));
        Assert.Throws<ArgumentException>(() => CaptureAnnotations.Validate(Spec()));
        Assert.Throws<ArgumentException>(() => CaptureAnnotations.Validate(Spec(crop: new(50, 50, 60, 50))));
        Assert.Throws<ArgumentException>(() => CaptureAnnotations.Validate(Spec(crop: new(10, 10, 60, 50), boxes: [new(50, 40, 20, 20)])));
        Assert.Throws<ArgumentException>(() => CaptureAnnotations.Validate(Spec(arrows: [new(10, 10, 15, 10)])));
        Assert.Throws<ArgumentException>(() => CaptureAnnotations.Validate(Spec(boxes: Enumerable.Repeat(new AnnotationRect(0, 0, 5, 5), 21).ToArray())));
        Assert.Throws<ArgumentException>(() => CaptureAnnotations.Validate(Spec() with { Source = "../L026" }));
    }

    [Fact]
    public void ArrowHeadPointsAtTheTarget()
    {
        var head = CaptureAnnotations.ArrowHead(new AnnotationArrow(10, 50, 60, 50));
        Assert.Equal(new PointF(60, 50), head[0]);
        Assert.Equal(new PointF(46, 58), head[1]);
        Assert.Equal(new PointF(46, 42), head[2]);
    }

    [Fact]
    public void RenderCropsThenDrawsRedactionsAndBoxes()
    {
        using var source = new Bitmap(100, 80);
        using (var graphics = Graphics.FromImage(source)) graphics.Clear(Color.White);
        using var output = CaptureAnnotations.Render(source, Spec(crop: new(10, 10, 60, 50), boxes: [new(5, 5, 20, 10)], redactions: [new(30, 30, 10, 10)]));
        Assert.Equal(new Size(60, 50), output.Size);
        Assert.Equal(CaptureAnnotations.BoxColor.ToArgb(), output.GetPixel(6, 10).ToArgb());
        Assert.Equal(CaptureAnnotations.RedactionColor.ToArgb(), output.GetPixel(35, 35).ToArgb());
        Assert.Equal(Color.White.ToArgb(), output.GetPixel(50, 5).ToArgb());
        Assert.Equal(Color.White.ToArgb(), source.GetPixel(15, 15).ToArgb());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PromotionKeepsTheCleanCaptureUnlessItWasRedacted(bool redacted)
    {
        var clean = Path.Combine(root, "L026.png");
        using (var bitmap = new Bitmap(100, 80)) bitmap.Save(clean, ImageFormat.Png);
        var docs = Path.Combine(root, "docs-copy");
        File.WriteAllText(Path.Combine(root, "manifest.json"), JsonSerializer.Serialize(new RunManifest
        {
            Passed = true, RequestedScenario = "backlog-dialogs", DocsRoot = docs, Output = root,
            Screenshots = new() { ["L026"] = clean }, ScreenshotHashes = new() { ["L026"] = ArtifactFiles.Sha256(clean) }
        }, RunConfig.Json));
        var backlog = Path.Combine(root, "backlog.json");
        File.WriteAllText(backlog, """[{"id":"L026","source":"content/assets/images/auto-formatting-settings.png","pages":[],"status":"replica-target"}]""");
        var spec = Path.Combine(root, "L026.annotations.json");
        File.WriteAllText(spec, JsonSerializer.Serialize(Spec(boxes: [new(5, 5, 20, 10)], redactions: redacted ? [new(30, 30, 10, 10)] : null), RunConfig.Json));

        CaptureAnnotations.Create(Path.Combine(root, "manifest.json"), spec);
        Assert.True(File.Exists(Path.Combine(root, "L026-annotated.png")));
        await ArtifactFiles.PromoteItem(root, "L026", false, backlog);

        var image = Path.Combine(docs, "content", "assets", "images", "auto-formatting-settings.png");
        Assert.True(File.Exists(image));
        Assert.Equal(!redacted, File.Exists(Path.ChangeExtension(image, ".clean.png")));
        Assert.Equal(!redacted, File.Exists(Path.ChangeExtension(image, ".annotations.json")));
    }
}
