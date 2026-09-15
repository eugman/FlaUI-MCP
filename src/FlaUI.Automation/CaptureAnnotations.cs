using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Json;
using System.Text.Json.Serialization;

public sealed record AnnotationRect(int X, int Y, int Width, int Height);
public sealed record AnnotationArrow(int FromX, int FromY, int ToX, int ToY);

/// <summary>
/// Light annotation of one clean checkpoint: an optional crop, then solid redactions, red boxes and green arrows.
/// Coordinates are in output (cropped) pixels. The clean checkpoint is never modified.
/// </summary>
public sealed record AnnotationSpec(int SchemaVersion, string Recipe, string Source, int ExpectedWidth, int ExpectedHeight,
    AnnotationRect? Crop = null, AnnotationRect[]? Boxes = null, AnnotationArrow[]? Arrows = null, AnnotationRect[]? Redactions = null)
{
    [JsonIgnore] public string Checkpoint => Source + "-annotated";
    [JsonIgnore] public bool Redacted => Redactions is { Length: > 0 };
}

/// <summary>Deliberately small: boxes, arrows and redaction only. Logos, callout art and styling stay a manual design step.</summary>
public static class CaptureAnnotations
{
    private const int MaxShapes = 20;
    public static readonly Color BoxColor = Color.FromArgb(220, 38, 38);
    public static readonly Color ArrowColor = Color.FromArgb(77, 182, 62);
    public static readonly Color RedactionColor = Color.FromArgb(64, 64, 64);

    public static void Validate(AnnotationSpec spec)
    {
        if (spec.SchemaVersion != 1 || string.IsNullOrWhiteSpace(spec.Recipe) || string.IsNullOrWhiteSpace(spec.Source) ||
            spec.Source.Any(c => char.IsControl(c) || Path.GetInvalidFileNameChars().Contains(c)))
            throw new ArgumentException("Invalid annotation spec");
        if (spec.ExpectedWidth is < 1 or > 8192 || spec.ExpectedHeight is < 1 or > 8192) throw new ArgumentException("Invalid expected source size");
        var crop = spec.Crop ?? new(0, 0, spec.ExpectedWidth, spec.ExpectedHeight);
        if (!Inside(crop, spec.ExpectedWidth, spec.ExpectedHeight)) throw new ArgumentException("Crop must be inside the source capture");
        var shapes = (spec.Boxes?.Length ?? 0) + (spec.Arrows?.Length ?? 0) + (spec.Redactions?.Length ?? 0);
        if (shapes == 0 && spec.Crop == null) throw new ArgumentException("Annotation spec changes nothing");
        if (shapes > MaxShapes) throw new ArgumentException($"At most {MaxShapes} shapes");
        foreach (var rect in (spec.Boxes ?? []).Concat(spec.Redactions ?? []))
            if (rect == null || rect.Width < 3 || rect.Height < 3 || !Inside(rect, crop.Width, crop.Height))
                throw new ArgumentException("Boxes and redactions must fit inside the output");
        foreach (var arrow in spec.Arrows ?? [])
            if (arrow == null || !InsidePoint(arrow.FromX, arrow.FromY, crop) || !InsidePoint(arrow.ToX, arrow.ToY, crop) ||
                Math.Sqrt(Math.Pow(arrow.ToX - arrow.FromX, 2) + Math.Pow(arrow.ToY - arrow.FromY, 2)) < 16)
                throw new ArgumentException("Arrows must fit inside the output and be at least 16 pixels long");
    }

    private static bool Inside(AnnotationRect r, int width, int height) =>
        r.X >= 0 && r.Y >= 0 && r.Width >= 1 && r.Height >= 1 && (long)r.X + r.Width <= width && (long)r.Y + r.Height <= height;
    private static bool InsidePoint(int x, int y, AnnotationRect crop) => x >= 0 && y >= 0 && x < crop.Width && y < crop.Height;

    /// <summary>Tip, then the two base corners of the head of an arrow pointing at (ToX, ToY).</summary>
    public static PointF[] ArrowHead(AnnotationArrow arrow, float length = 14, float halfWidth = 8)
    {
        float dx = arrow.ToX - arrow.FromX, dy = arrow.ToY - arrow.FromY;
        var norm = MathF.Sqrt(dx * dx + dy * dy);
        float ux = dx / norm, uy = dy / norm;
        float baseX = arrow.ToX - ux * length, baseY = arrow.ToY - uy * length;
        return [new(arrow.ToX, arrow.ToY), new(baseX - uy * halfWidth, baseY + ux * halfWidth), new(baseX + uy * halfWidth, baseY - ux * halfWidth)];
    }

    public static Bitmap Render(Bitmap source, AnnotationSpec spec)
    {
        Validate(spec);
        if (source.Width != spec.ExpectedWidth || source.Height != spec.ExpectedHeight) throw new ArgumentException("Source geometry changed");
        var crop = spec.Crop ?? new(0, 0, source.Width, source.Height);
        var output = source.Clone(new Rectangle(crop.X, crop.Y, crop.Width, crop.Height), PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(output);
        using (var fill = new SolidBrush(RedactionColor))
            foreach (var r in spec.Redactions ?? []) graphics.FillRectangle(fill, r.X, r.Y, r.Width, r.Height);
        using (var pen = new Pen(BoxColor, 2))
            foreach (var r in spec.Boxes ?? []) graphics.DrawRectangle(pen, r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var pen = new Pen(ArrowColor, 4))
        using (var brush = new SolidBrush(ArrowColor))
            foreach (var arrow in spec.Arrows ?? [])
            {
                var head = ArrowHead(arrow);
                graphics.DrawLine(pen, arrow.FromX, arrow.FromY, (head[1].X + head[2].X) / 2, (head[1].Y + head[2].Y) / 2);
                graphics.FillPolygon(brush, head);
            }
        return output;
    }

    /// <summary>Writes SOURCE-annotated.png next to the clean checkpoint and records it in the manifest.</summary>
    public static void Create(string manifestPath, string specPath)
    {
        var manifest = ArtifactFiles.ReadManifest(manifestPath);
        var spec = JsonSerializer.Deserialize<AnnotationSpec>(File.ReadAllText(specPath), RunConfig.Json) ?? throw new ArgumentException("Missing annotation spec");
        Validate(spec);
        if (!manifest.Passed || manifest.NeedsRecovery || manifest.RequestedScenario != spec.Recipe)
            throw new ArgumentException("Annotation requires a passed run of the matching recipe");
        if (!manifest.Screenshots.TryGetValue(spec.Source, out var sourcePath)) throw new ArgumentException("Missing checkpoint: " + spec.Source);
        var runDirectory = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
        sourcePath = ArtifactFiles.ContainedPath(sourcePath, runDirectory);
        if (manifest.ScreenshotHashes.TryGetValue(spec.Source, out var captured) && ArtifactFiles.Sha256(sourcePath) != captured)
            throw new IOException("Clean checkpoint changed since capture");
        var output = Path.Combine(runDirectory, spec.Checkpoint + ".png");
        if (manifest.Screenshots.ContainsKey(spec.Checkpoint) || File.Exists(output)) throw new IOException("Annotated checkpoint already exists");
        using (var source = new Bitmap(sourcePath))
        using (var annotated = Render(source, spec))
        using (var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write))
            annotated.Save(file, ImageFormat.Png);
        manifest.Screenshots.Add(spec.Checkpoint, output);
        manifest.ScreenshotHashes.Add(spec.Checkpoint, ArtifactFiles.Sha256(output));
        manifest.Annotations.Add(spec.Checkpoint, spec);
        AtomicJournal.Write(manifestPath, manifest, RunConfig.Json);
        manifest.Output = runDirectory;
        ArtifactFiles.WriteIndex(manifest);
    }
}
