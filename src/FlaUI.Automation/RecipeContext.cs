using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

public sealed class RecipeContext(Te3Page page, RunConfig config, RunManifest manifest, string model, Action save)
{
    public Te3Page Page { get; } = page;
    private CaptureEnvironment BeforeCapture()
    {
        var environment = Page.ObserveCaptureEnvironment();
        environment.RequireScale(config.ExpectedDpi);
        return environment;
    }
    private void RecordCapture(string checkpoint, string path, CaptureEnvironment before)
    {
        var after = Page.ObserveCaptureEnvironment();
        if (before != after) throw new InvalidOperationException("Display context changed during capture; image is not a verified checkpoint");
        manifest.CaptureEnvironments.Add(checkpoint, after);
        manifest.Screenshots.Add(checkpoint, path);
        save();
    }
    public async Task WaitForAgent()
    {
        if (manifest.Database.Length != 0) throw new InvalidOperationException("Agent trials must be offline");
        manifest.CurrentStep = "Agent handoff: waiting for operator release";
        save();
        var ready = Path.Combine(manifest.Output, "agent-ready.json");
        var release = Path.Combine(manifest.Output, "agent-release.txt");
        if (File.Exists(ready) || File.Exists(release)) throw new IOException("Handoff files already exist");
        File.WriteAllText(ready, JsonSerializer.Serialize(new { manifest.RunId, manifest.ProcessId,
            screenshot = Path.Combine(manifest.Output, "agent-code-actions.png"),
            expiresUtc = DateTime.UtcNow.AddMinutes(10) }, RunConfig.Json));
        Console.WriteLine("Agent handoff ready: " + ready);
        await AgentHandoff.Wait(() => File.Exists(release) ? File.ReadAllText(release).Trim() : null, TimeSpan.FromMinutes(10));
    }
    public Task MapSerializationModes() => Page.SaveSerializationModes(Path.Combine(manifest.Output, "serialization-modes.uia.json"));
    public async Task CaptureLanguageChoices()
    {
        const string checkpoint = "preferences-language-choices";
        var path = Path.Combine(manifest.Output, checkpoint + ".png");
        if (manifest.Screenshots.ContainsKey(checkpoint) || File.Exists(path)) throw new IOException("Duplicate checkpoint: " + checkpoint);
        var environment = BeforeCapture();
        await Page.CaptureLanguageChoices(path);
        RecordCapture(checkpoint, path, environment);
    }
    public Task MapControls(string name, PlaywrightWindows.Mcp.Core.ElementSelector within)
        => Page.SaveControlMap(Path.Combine(manifest.Output, name + ".uia.json"), within);
    private readonly CliRunner cli = new();
    private string[] Source => manifest.Database.Length == 0 ? [model] : ["-s", config.Server, "-d", manifest.Database];
    public async Task Capture(string checkpoint, Te3Page.Target? target = null,
        PlaywrightWindows.Mcp.Core.CaptureFrame? frame = null, bool scalarScene = false, bool screenPixels = false)
    {
        var path = Path.Combine(manifest.Output, checkpoint + ".png");
        if (manifest.Screenshots.ContainsKey(checkpoint) || File.Exists(path)) throw new IOException("Duplicate checkpoint: " + checkpoint);
        var environment = BeforeCapture();
        if (scalarScene)
        {
            if (target != null || frame != null || screenPixels) throw new ArgumentException("Scalar scene owns its framing and capture mode");
            await Page.CaptureScalarScene(path);
        }
        else await Page.Capture(path, target, frame, screenPixels);
        RecordCapture(checkpoint, path, environment);
    }
    public async Task CaptureCalculationGroupMenu()
    {
        const string checkpoint = "model-calculation-group-menu";
        var path = Path.Combine(manifest.Output, checkpoint + ".png");
        if (manifest.Screenshots.ContainsKey(checkpoint) || File.Exists(path)) throw new IOException("Duplicate checkpoint: " + checkpoint);
        var environment = BeforeCapture();
        await Page.CaptureCalculationGroupMenu(path);
        RecordCapture(checkpoint, path, environment);
    }
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
