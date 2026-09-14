using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;

public sealed record CompositionPanel(string Checkpoint, string Label, int ExpectedWidth, int ExpectedHeight,
    int X, int Y, int Width, int Height);
public sealed record CompositionOutline(int X, int Y, int Width, int Height);
public sealed record CompositionArrow(int TailX, int TipX, int Y);
public sealed record CompositionSpec(int SchemaVersion, string Recipe, string Checkpoint, CompositionPanel[] Panels,
    bool CropOnly = false, CompositionOutline? Outline = null, CompositionArrow[]? Arrows = null);

/// <summary>Crop and vertically stack existing panels; no resampling or approval claims.</summary>
public static class CaptureComposition
{
    public static void Create(string manifestPath, string specPath, string output)
    {
        var manifest = ArtifactFiles.ReadManifest(manifestPath);
        var spec = JsonSerializer.Deserialize<CompositionSpec>(File.ReadAllText(specPath), RunConfig.Json) ?? throw new ArgumentException("Missing composition spec");
        Validate(spec);
        if (!manifest.Passed || manifest.NeedsRecovery || manifest.RequestedScenario != spec.Recipe)
            throw new ArgumentException("Composition requires a passed matching recipe");
        var runDirectory = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
        output = ArtifactFiles.ContainedPath(output, runDirectory);
        if (!output.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Composition output must be PNG");
        if (manifest.Screenshots.ContainsKey(spec.Checkpoint) || File.Exists(output))
            throw new IOException("Composition checkpoint or output already exists");
        void RegisterOutput()
        {
            manifest.Screenshots.Add(spec.Checkpoint, output);
            manifest.ScreenshotHashes.Add(spec.Checkpoint, ArtifactFiles.Sha256(output));
            manifest.Compositions.Add(spec.Checkpoint, spec);
            AtomicJournal.Write(manifestPath, manifest, RunConfig.Json);
            // Index rendering uses the actual manifest location, never a stale output path.
            manifest.Output = runDirectory;
            ArtifactFiles.WriteIndex(manifest);
        }
        var images = new List<Bitmap>();
        try
        {
            foreach (var panel in spec.Panels)
            {
                if (!manifest.Screenshots.TryGetValue(panel.Checkpoint, out var path)) throw new ArgumentException("Missing checkpoint: " + panel.Checkpoint);
                using var bitmap = new Bitmap(ArtifactFiles.ContainedPath(path, Path.GetDirectoryName(Path.GetFullPath(manifestPath))!));
                if (bitmap.Width != panel.ExpectedWidth || bitmap.Height != panel.ExpectedHeight) throw new ArgumentException("Source geometry changed");
                images.Add(bitmap.Clone(new Rectangle(panel.X, panel.Y, panel.Width, panel.Height), PixelFormat.Format32bppArgb));
            }
            if (spec.CropOnly)
            {
                if (spec.Outline is { } outline)
                {
                    // Explicit post-capture annotation; the screenshot pixels are not resized.
                    using var graphics = Graphics.FromImage(images[0]);
                    using var pen = new Pen(Color.Red, 2);
                    graphics.DrawRectangle(pen, outline.X + 1, outline.Y + 1, outline.Width - 2, outline.Height - 2);
                }
                if (spec.Arrows is { Length: > 0 } arrows)
                {
                    using var graphics = Graphics.FromImage(images[0]);
                    using var pen = new Pen(Color.FromArgb(77, 182, 62), 5);
                    using var brush = new SolidBrush(pen.Color);
                    foreach (var arrow in arrows)
                    {
                        graphics.DrawLine(pen, arrow.TailX, arrow.Y, arrow.TipX + 10, arrow.Y);
                        graphics.FillPolygon(brush, new Point[] { new(arrow.TipX, arrow.Y),
                            new(arrow.TipX + 12, arrow.Y - 7), new(arrow.TipX + 12, arrow.Y + 7) });
                    }
                }
                using (var cropFile = new FileStream(output, FileMode.CreateNew, FileAccess.Write))
                    images[0].Save(cropFile, ImageFormat.Png);
                RegisterOutput();
                return;
            }
            using var canvas = new Bitmap(spec.Panels.Max(p => p.Width) + 48, spec.Panels.Sum(p => p.Height + 68) + 24);
            canvas.SetResolution(96, 96);
            using (var graphics = Graphics.FromImage(canvas))
            using (var font = new Font("Segoe UI", 18, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                graphics.Clear(Color.White); var y = 24;
                for (var n = 0; n < images.Count; n++)
                {
                    if (graphics.MeasureString(spec.Panels[n].Label, font).Width > canvas.Width - 48) throw new ArgumentException("Composition label clips");
                    graphics.DrawString(spec.Panels[n].Label, font, Brushes.Black, 24, y); y += 44;
                    graphics.DrawImageUnscaled(images[n], 24, y); y += images[n].Height + 24;
                }
            }
            using (var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write))
                canvas.Save(file, ImageFormat.Png);
            RegisterOutput();
        }
        finally { foreach (var image in images) image.Dispose(); }
    }
    public static void Validate(CompositionSpec spec)
    {
        if (spec.SchemaVersion != 1 || string.IsNullOrWhiteSpace(spec.Recipe) || string.IsNullOrWhiteSpace(spec.Checkpoint) ||
            spec.Panels is not { Length: > 0 and <= 8 }) throw new ArgumentException("Invalid composition spec");
        if (spec.CropOnly && spec.Panels.Length != 1) throw new ArgumentException("Crop-only output requires exactly one panel");
        foreach (var p in spec.Panels)
            if (p == null || string.IsNullOrWhiteSpace(p.Checkpoint) || string.IsNullOrWhiteSpace(p.Label) || p.Label.Any(char.IsControl) ||
                p.ExpectedWidth is < 1 or > 8192 || p.ExpectedHeight is < 1 or > 8192 || p.X < 0 || p.Y < 0 || p.Width < 1 || p.Height < 1 ||
                (long)p.X + p.Width > p.ExpectedWidth || (long)p.Y + p.Height > p.ExpectedHeight)
                throw new ArgumentException("Invalid panel geometry");
        if (spec.Outline is { } outline && (!spec.CropOnly || outline.X < 0 || outline.Y < 0 ||
            outline.Width < 3 || outline.Height < 3 ||
            (long)outline.X + outline.Width > spec.Panels[0].Width || (long)outline.Y + outline.Height > spec.Panels[0].Height))
            throw new ArgumentException("Outline must be contained in a single crop-only panel");
        if (spec.Arrows is { Length: > 0 } arrows && (!spec.CropOnly || arrows.Length > 8 || arrows.Any(a =>
            a == null || a.TipX < 0 || (long)a.TipX + 16 > a.TailX ||
            a.TailX > spec.Panels[0].Width - 4 || a.Y < 8 || a.Y > spec.Panels[0].Height - 9)))
            throw new ArgumentException("Left arrows must fit inside a single crop-only panel");
        if (spec.Panels.Sum(p => (long)p.Height + 68) > 16000) throw new ArgumentException("Composition exceeds height budget");
    }
}
