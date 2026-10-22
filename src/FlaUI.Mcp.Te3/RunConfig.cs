using System.Text.Json;
using FlaUI.Mcp.Core;

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
    public static JsonSerializerOptions Json { get; } =
        new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };

    public static RunConfig Read(string path)
    {
        var config =
            JsonSerializer.Deserialize<RunConfig>(File.ReadAllText(path), Json)
            ?? throw new ArgumentException("Missing config");
        var folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
        // Core's HostPath: a local, fully qualified Windows path, %NAME% expanded when defined; a plain relative path
        // is taken from the config's folder first (its .. segments, as in the default output, resolved there). Anything else (UNC, ~, /mnt/..., $NAME) is refused by field name.
        string Resolve(string value, string field) =>
            HostPath.Validate(Relative(value) ? Path.GetFullPath(value, folder) : value, field);
        if (config.Repeat is < 1 or > 100 || config.FixtureMode is not ("auto" or "fixed" or "offline"))
        {
            throw new ArgumentException("repeat must be 1..100; fixtureMode must be auto, fixed or offline");
        }

        if (!config.FixedSlot.StartsWith("fla_", StringComparison.Ordinal) || config.FixedSlot.Any(char.IsControl))
        {
            throw new ArgumentException("Existing engine database must use the fla_ prefix");
        }

        if (config.ExpectedDpi is <= 0 or > 768)
        {
            throw new ArgumentException("expectedDpi must be 1..768");
        }

        if (string.IsNullOrWhiteSpace(config.Te3) || string.IsNullOrWhiteSpace(config.Te))
        {
            throw new ArgumentException("te3 and te executable paths are required");
        }

        return config with
        {
            Te3 = Resolve(config.Te3, "te3"),
            Te = Resolve(config.Te, "te"),
            FixtureScript = Resolve(config.FixtureScript, "fixtureScript"),
            OfflineBaseline =
                config.OfflineBaseline == null ? null : Resolve(config.OfflineBaseline, "offlineBaseline"),
            Output = Resolve(config.Output, "output"),
            DocsRoot = config.DocsRoot == null ? null : Resolve(config.DocsRoot, "docsRoot"),
        };
    }

    // Relative to the config's folder: not rooted, and none of the forms HostPath refuses on its own merits.
    private static bool Relative(string value) =>
        !Path.IsPathRooted(value) && !value.StartsWith('~') && !value.StartsWith('%') && !value.StartsWith('$');

    public void ValidateFiles(IEnumerable<Recipe> recipes)
    {
        var selected = recipes.ToArray();
        var paths = new List<string> { Te3, Te };
        if (OfflineBaseline == null || selected.Any(UseServer))
        {
            paths.Add(FixtureScript);
        }

        if (OfflineBaseline != null && selected.Any(recipe => !UseServer(recipe)))
        {
            paths.Add(OfflineBaseline);
        }

        foreach (var path in paths)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Required runner input is missing", path);
            }
        }
    }

    public bool UseServer(Recipe recipe) => FixtureMode == "fixed" || FixtureMode == "auto" && recipe.RequiresServer;
}
