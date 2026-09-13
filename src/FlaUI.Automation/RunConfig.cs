using System.Text.Json;

public sealed record RunConfig
{
    public string Te3 { get; init; } = "";
    public string Te { get; init; } = "";
    public string FixtureScript { get; init; } = "fixture.csx";
    public string? OfflineBaseline { get; init; }
    public string FixtureMode { get; init; } = "auto";
    public string Server { get; init; } = "localhost";
    public string FixedSlot { get; init; } = "fla_te3_small";
    public string Output { get; init; } = "../artifacts/te3";
    public string? DocsRoot { get; init; }
    public int Repeat { get; init; } = 1;
    public bool MaximizeWindow { get; init; } = true;
    public string? CaptureVariant { get; init; }
    public int? ExpectedDpi { get; init; }
    public string[] Scenarios { get; init; } = RecipeCatalog.SmokeIds;
    public static JsonSerializerOptions Json { get; } = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public static RunConfig Read(string path)
    {
        var config = JsonSerializer.Deserialize<RunConfig>(File.ReadAllText(path), Json) ?? throw new ArgumentException("Missing config");
        string Resolve(string value) => Path.GetFullPath(Environment.ExpandEnvironmentVariables(value), Path.GetDirectoryName(Path.GetFullPath(path))!);
        if (config.Repeat is < 1 or > 100 || config.FixtureMode is not ("auto" or "fixed" or "offline"))
            throw new ArgumentException("repeat must be 1..100; fixtureMode must be auto, fixed or offline");
        if (!config.FixedSlot.StartsWith("fla_", StringComparison.Ordinal) || config.FixedSlot.Any(char.IsControl))
            throw new ArgumentException("Existing engine database must use the fla_ prefix");
        if (config.ExpectedDpi is <= 0 or > 768) throw new ArgumentException("expectedDpi must be 1..768");
        if (string.IsNullOrWhiteSpace(config.Te3) || string.IsNullOrWhiteSpace(config.Te)) throw new ArgumentException("te3 and te executable paths are required");
        return config with { Te3 = Resolve(config.Te3), Te = Resolve(config.Te), FixtureScript = Resolve(config.FixtureScript),
            OfflineBaseline = config.OfflineBaseline == null ? null : Resolve(config.OfflineBaseline),
            Output = Resolve(config.Output), DocsRoot = config.DocsRoot == null ? null : Resolve(config.DocsRoot) };
    }
    public void ValidateFiles()
    {
        foreach (var path in new[] { Te3, Te, OfflineBaseline ?? FixtureScript })
            if (!File.Exists(path)) throw new FileNotFoundException("Required runner input is missing", path);
    }
    public bool UseServer(Recipe recipe) => FixtureMode == "fixed" || FixtureMode == "auto" && recipe.RequiresServer;
}
