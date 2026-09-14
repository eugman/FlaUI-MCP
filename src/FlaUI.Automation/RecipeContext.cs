using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

public sealed class RecipeContext(Te3Page page, RunConfig config, RunManifest manifest, string model, Action save)
{
    public Te3Page Page { get; } = page;
    // Every checkpoint is new, captured at the expected DPI, and in an unchanged display context.
    private async Task CaptureCheckpoint(string checkpoint, Func<string, Task> capture)
    {
        var path = Path.Combine(manifest.Output, checkpoint + ".png");
        if (manifest.Screenshots.ContainsKey(checkpoint) || File.Exists(path)) throw new IOException("Duplicate checkpoint: " + checkpoint);
        var before = Page.ObserveCaptureEnvironment();
        before.RequireScale(config.ExpectedDpi);
        await capture(path);
        var after = Page.ObserveCaptureEnvironment();
        if (before != after) throw new InvalidOperationException("Display context changed during capture; image is not a verified checkpoint");
        manifest.CaptureEnvironments.Add(checkpoint, after);
        manifest.Screenshots.Add(checkpoint, path);
        manifest.ScreenshotHashes.Add(checkpoint, ArtifactFiles.Sha256(path));
        save();
    }
    public Task MapSerializationModes() => Page.SaveSerializationModes(Path.Combine(manifest.Output, "serialization-modes.uia.json"));
    public Task CaptureLanguageChoices() => CaptureCheckpoint("preferences-language-choices", Page.CaptureLanguageChoices);
    public Task MapControls(string name, PlaywrightWindows.Mcp.Core.ElementSelector within)
        => Page.SaveControlMap(Path.Combine(manifest.Output, name + ".uia.json"), within);
    private readonly CliRunner cli = new();
    private string[] Source => manifest.Database.Length == 0 ? [model] : ["-s", config.Server, "-d", manifest.Database];
    public Task Capture(string checkpoint, Te3Page.Target? target = null,
        PlaywrightWindows.Mcp.Core.CaptureFrame? frame = null, bool scalarScene = false)
    {
        if (scalarScene && (target != null || frame != null)) throw new ArgumentException("Scalar scene owns its framing");
        return CaptureCheckpoint(checkpoint, path => scalarScene ? Page.CaptureScalarScene(path) : Page.Capture(path, target, frame));
    }
    public Task CaptureCalculationGroupMenu() => CaptureCheckpoint("model-calculation-group-menu", Page.CaptureCalculationGroupMenu);
    public async Task LoadScript(string name, string source)
    {
        var path = Path.Combine(manifest.Output, name + ".csx");
        var bytes = new UTF8Encoding(false).GetBytes(source);
        if (File.Exists(path))
        {
            if (!bytes.AsSpan().SequenceEqual(File.ReadAllBytes(path))) throw new IOException("Script changed within run");
        }
        else
        {
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            file.Write(bytes);
        }
        manifest.SourceHashes[name + ".csx"] = Convert.ToHexString(SHA256.HashData(bytes));
        await Page.OpenCSharpScriptFile(path, source);
    }
    public async Task Query(string dax, decimal expected)
    {
        manifest.CurrentStep = "Verify independent query result";
        if (manifest.Database.Length == 0) throw new InvalidOperationException("This recipe requires an engine fixture");
        var result = await cli.Run(config.Te, ["query", "-s", config.Server, "-d", manifest.Database, "-q", dax]);
        QueryAssertions.VerifyScalar(result, expected);
    }
    public async Task Metadata(string table, string kind, string name, string property, string expected)
    {
        manifest.CurrentStep = $"Verify persisted {table}/{name}/{property}";
        if (kind is not ("Measure" or "Column") || property is not ("Description" or "FormatString" or "DisplayFolder"))
            throw new ArgumentException("Unsupported metadata assertion");
        string Q(string value) => JsonSerializer.Serialize(value);
        var script = $"var o = Model.Tables[{Q(table)}].{(kind == "Measure" ? "Measures" : "Columns")}[{Q(name)}]; if (Convert.ToString(o.{property}, System.Globalization.CultureInfo.InvariantCulture) != {Q(expected)}) throw new Exception(\"Metadata assertion failed\");";
        await cli.Run(config.Te, new[] { "script" }.Concat(Source).Concat(["-e", script]));
        manifest.ModelChecks[table + "/" + name + "/" + property] = expected;
    }
    public async Task Unchanged(Func<Task> action)
    {
        async Task<string> Snapshot(string name)
        {
            manifest.CurrentStep = $"Read persisted model: {name}";
            var path = Path.Combine(manifest.Output, name + ".bim");
            await cli.Run(config.Te, new[] { "save" }.Concat(Source).Concat(["-o", path, "--serialization", "bim", "--skip-validation", "--skip-bpa"]));
            return ModelStateVerification.Hash(File.ReadAllText(path));
        }
        var before = await Snapshot("model-before");
        await action();
        await Page.SelectDocument("Expression Editor");
        await Page.Save();
        var after = await Snapshot("model-after");
        if (before != after) throw new InvalidOperationException("Persisted model metadata changed");
        manifest.ModelChecks["unchanged"] = after;
    }
}
