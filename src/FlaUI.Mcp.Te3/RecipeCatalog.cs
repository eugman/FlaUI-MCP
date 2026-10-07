public sealed record Recipe(string Id, string DocsPage, bool RequiresServer, Func<RecipeContext, Task> Execute);

/// <summary>
/// Every recipe establishes its own prerequisites in a freshly reset model.
/// The tracked recipes are the smoke set: they exercise the runner against any TE3 install.
/// Your own recipes go in local/recipes as another part of this partial class (see automation/README.md).
/// </summary>
public static partial class RecipeCatalog
{
    private const string DaxPage = "content/getting-started/creating-and-testing-dax.md";
    private const string CSharpPage = "content/features/csharp-scripts.md";

    public static string[] SmokeIds =>
        [
            "model-open",
            "measure-edit",
            "dax-query",
            "expression-recovery",
            "qualified-navigation",
            "bpa-correction",
            "properties",
            "csharp-preview",
        ];

    public static IReadOnlyList<Recipe> All { get; } = Build();

    public static Recipe[] Select(IEnumerable<string> ids)
    {
        var selected = ids.Select(Find).ToArray();
        if (selected.Length == 0 || selected.Select(r => r.Id).Distinct().Count() != selected.Length)
        {
            throw new ArgumentException("Select at least one recipe without duplicates");
        }

        return selected;
    }

    private static Recipe Find(string id) =>
        All.SingleOrDefault(r => r.Id == id) ?? throw new ArgumentException("Unknown recipe: " + id);

    private static List<Recipe> Build()
    {
        var recipes = new List<Recipe>
        {
            new("model-open", DaxPage, false, ModelOpen),
            new("measure-edit", DaxPage, true, MeasureEdit),
            new("dax-query", "content/features/dax-query.md", true, DaxQuery),
            new("expression-recovery", DaxPage, true, ExpressionRecovery),
            new("qualified-navigation", "content/how-tos/edit-properties.md", false, QualifiedNavigation),
            new("bpa-correction", "content/features/Best-Practice-Analyzer.md", true, BpaCorrection),
            new("properties", "content/how-tos/edit-properties.md", true, Properties),
            new("csharp-preview", CSharpPage, true, Preview),
        };
        AddLocal(recipes);
        return recipes;
    }

    /// <summary>Implemented by local/recipes when present; otherwise compiled away.</summary>
    static partial void AddLocal(List<Recipe> recipes);

    private static async Task Open(RecipeContext c)
    {
        await c.Page.OpenTom();
        await c.Page.ResetSearch();
        await c.Page.SelectTable("Sales");
        await c.Page.ExpandSelected();
    }

    private static async Task Revenue(RecipeContext c)
    {
        await Open(c);
        await c.Page.SelectObjectPath("Sales", "UI Revenue", "Measure");
        await c.Page.SetExpression("[Total Amount] + 1");
        await c.Page.Save();
        await c.Query("EVALUATE { [UI Revenue] }", 61);
    }

    private static async Task ModelOpen(RecipeContext c)
    {
        await Open(c);
        await c.Capture("model-open");
    }

    private static async Task MeasureEdit(RecipeContext c)
    {
        await Revenue(c);
        await c.Capture("measure-editor");
    }

    private static async Task DaxQuery(RecipeContext c)
    {
        await Revenue(c);
        await c.Page.NewQuery();
        await c.Page.RunQuery("EVALUATE { [UI Revenue] }");
        await c.Page.AssertQueryResult("61");
        await c.Capture("query-result");
    }

    private static async Task ExpressionRecovery(RecipeContext c)
    {
        await Revenue(c);
        await c.Page.SetExpression("[Missing Measure]");
        await c.Page.AssertMissingMeasureError();
        await c.Capture("expression-error");

        // SetExpression presses F5 to refresh validation before settled absence.
        await c.Page.SetExpression("[Total Amount] + 2");
        await c.Page.AssertNoErrors();
        await c.Page.Save();
        await c.Query("EVALUATE { [UI Revenue] }", 62);
        await c.Capture("expression-recovered");
    }

    private static async Task QualifiedNavigation(RecipeContext c)
    {
        await c.Page.SelectObjectPath("Sales", "Amount", "Column");
        await c.Page.SelectObjectPath("Comparison", "Amount", "Column");
        await c.Page.SelectObjectPath("Sales", "Total Amount", "Measure", "Smoke tests");
        await c.Capture("qualified-navigation");
    }

    private static async Task BpaCorrection(RecipeContext c)
    {
        const string Rule = "fla_ UI description required (1 object)";
        await Revenue(c);
        await ShowBpa(c);
        await c.Page.AssertBpaViolation(Rule);
        await c.Capture("bpa-violation");

        await c.Page.SelectObjectPath("Sales", "UI Revenue", "Measure");
        await c.Page.SetProperty("Description", "Documented test revenue");
        await c.Page.Save();
        await c.Metadata("Sales", "Measure", "UI Revenue", "Description", "Documented test revenue");

        await ShowBpa(c);
        await c.Page.AssertBpaClear(Rule);
        await c.Capture("bpa-corrected");
    }

    private static async Task ShowBpa(RecipeContext c)
    {
        await c.Page.OpenBpa();
        await c.Page.RefreshBpa();
        await c.Page.ShowBpaBottom();
    }

    private static async Task ConfigureProperties(RecipeContext c)
    {
        await c.Page.SelectObjectPath("Sales", "UI Revenue", "Measure");
        await c.Page.SetProperty("Format String", "#,##0.00");
        await c.Page.SetProperty("Display Folder", @"Automation\Verified");
        await c.Page.SelectObjectPath("Sales", "UI Revenue", "Measure", @"Automation\Verified");
        await c.Page.Save();
        await c.Metadata("Sales", "Measure", "UI Revenue", "DisplayFolder", @"Automation\Verified");
        await c.Metadata("Sales", "Measure", "UI Revenue", "FormatString", "#,##0.00");
    }

    private static async Task Properties(RecipeContext c)
    {
        await Revenue(c);
        await ConfigureProperties(c);
        await c.Capture("property-editor");
    }

    private static async Task Preview(RecipeContext c)
    {
        await Revenue(c);
        await ConfigureProperties(c);
        await c.Page.SetProperty("Description", "Documented test revenue");
        await c.Page.Save();

        await c.LoadScript(
            "preview",
            "Model.Tables[\"Sales\"].Measures[\"UI Revenue\"].Description = \"Accepted script change\";"
        );
        await c.Page.PreviewCSharpScript();
        await c.Page.ShowPreviewDescription("UI Revenue", "Documented test revenue", "Accepted script change");
        await c.Capture("script-preview", Te3Page.ScriptPreviewTarget());

        await c.Page.CancelScriptPreview();
        await c.Page.AssertProperty("Description", "Documented test revenue");

        await c.Page.PreviewCSharpScript();
        await c.Page.AcceptScriptPreview();
        await c.Page.AssertProperty("Description", "Accepted script change");
        await c.Page.SelectDocument("Expression Editor");
        await c.Page.Save();
        await c.Metadata("Sales", "Measure", "UI Revenue", "Description", "Accepted script change");
        await c.Capture("script-accepted");
    }
}
