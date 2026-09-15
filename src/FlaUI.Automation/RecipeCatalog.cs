public sealed record Recipe(string Id, string DocsPage, bool RequiresServer, Func<RecipeContext, Task> Execute);

/// <summary>Every recipe establishes its own prerequisites in a freshly reset model.</summary>
public static class RecipeCatalog
{
    private const string DaxPage = "content/getting-started/creating-and-testing-dax.md";
    private const string CSharpPage = "content/features/csharp-scripts.md";
    public static string[] SmokeIds => ["model-open", "measure-edit", "dax-query", "expression-recovery",
        "qualified-navigation", "bpa-correction", "properties", "csharp-preview"];
    public static IReadOnlyList<Recipe> All { get; } =
    [
        new("model-open", DaxPage, false, ModelOpen),
        new("preferences-map", "content/references/preferences.md", false, PreferencesMap),
        new("preferences-language-choices", "content/references/application-language.md", false, LanguageChoices),
        new("preferences-auto-formatting", "content/references/preferences.md", false, AutoFormatting),
        new("preferences-code-actions", "content/features/code-actions.md", false, CodeActionsPreferences),
        new("preferences-dax-general", "content/references/preferences.md", false, DaxGeneralPreferences),
        new("preferences-file-formats", "content/references/preferences.md", false, FileFormatsPreferences),
        new("preferences-save-to-folder", "content/getting-started/parallel-development.md", false, SaveToFolderPreferences),
        new("model-calculation-group-menu", "content/getting-started/migrate-from-desktop.md", false, CalculationGroupMenu),
        new("measure-edit", DaxPage, true, MeasureEdit),
        new("dax-query", "content/features/dax-query.md", true, DaxQuery),
        new("expression-recovery", DaxPage, true, ExpressionRecovery),
        new("qualified-navigation", "content/how-tos/edit-properties.md", false, QualifiedNavigation),
        new("bpa-correction", "content/features/Best-Practice-Analyzer.md", true, BpaCorrection),
        new("properties", "content/how-tos/edit-properties.md", true, Properties),
        new("csharp-preview", CSharpPage, true, Preview),
        new("csharp-preview-page", CSharpPage, false, PreviewPage),
        new("csharp-output-scalar", CSharpPage, false, ScalarOutput),
        new("csharp-scalar-scene", CSharpPage, false, ScalarScene),
        new("csharp-output-object", CSharpPage, false, ObjectOutput),
        new("csharp-output-values", CSharpPage, false, ValuesOutput),
        new("file-load-three-cycles", CSharpPage, false, FileLoad),
        new("csharp-auto-rollback-source", CSharpPage, false, AutoRollbackSource),
        new("discovery-backlog", "automation/RECIPES.md", false, DiscoveryRecipes.Backlog),
        new("discovery-backlog-2", "automation/RECIPES.md", false, DiscoveryRecipes.Backlog2),
        new("backlog-menus", "content/features/views/user-interface.md", false, BacklogRecipes.MenuRecipes),
        new("backlog-dialogs", "content/features/views/user-interface.md", false, BacklogRecipes.DialogRecipes),
        new("backlog-preferences-1", "content/references/preferences.md", false, BacklogRecipes.PreferenceRecipes)
    ];

    public static Recipe[] Select(IEnumerable<string> ids)
    {
        var selected = ids.Select(id => All.SingleOrDefault(r => r.Id == id) ?? throw new ArgumentException("Unknown recipe: " + id)).ToArray();
        if (selected.Length == 0 || selected.Select(r => r.Id).Distinct().Count() != selected.Length)
            throw new ArgumentException("Select at least one recipe without duplicates");
        return selected;
    }

    private static async Task Open(RecipeContext c)
    {
        await c.Page.AssertLoaded("Sales"); await c.Page.SelectObject("Sales"); await c.Page.ExpandSelected();
    }
    private static async Task Revenue(RecipeContext c)
    {
        await Open(c); await c.Page.CreateMeasure(); await c.Page.RenameNewMeasure("UI Revenue");
        await c.Page.SetExpression("[Total Amount] + 1"); await c.Page.Save();
        await c.Query("EVALUATE { [UI Revenue] }", 61);
    }
    private static async Task ModelOpen(RecipeContext c) { await Open(c); await c.Capture("model-open"); }
    private static async Task PreferencesMap(RecipeContext c)
    {
        await c.Unchanged(async () =>
        {
            await c.Page.OpenPreferences();
            await c.MapControls("preferences-tree", new(AutomationId: "treePreferences"));
            await c.Capture("preferences", Te3Page.PreferencesTarget());
            await c.Page.CancelPreferences();
        });
    }
    private static async Task LanguageChoices(RecipeContext c)
    {
        await c.Unchanged(async () =>
        {
            await c.Page.OpenPreferences();
            await c.Page.SizePreferences(1155, 714);
            await c.Page.SearchPreferences("User Interface");
            await c.Page.SelectPreferencesSection("User Interface");
            await c.MapControls("preferences-language-pane", new(AutomationId: "Tabular Editor.User Interface"));
            await c.CaptureLanguageChoices();
            await c.Page.CancelPreferences();
        });
    }
    private static async Task AutoFormatting(RecipeContext c)
    {
        await c.Unchanged(async () =>
        {
            await c.Page.OpenPreferences();
            await c.Page.SizePreferences(1155, 714);
            await c.Page.SearchPreferences("Auto Formatting");
            await c.MapControls("preferences-search", new(AutomationId: "treePreferences"));
            await c.Page.SelectPreferencesSection("Auto Formatting");
            await c.Page.AssertAutoFormattingState();
            await c.MapControls("auto-formatting", new(AutomationId: "DAX Editor.Auto Formatting"));
            await c.MapControls("preferences-tree", new(AutomationId: "treePreferences"));
            await c.Capture("preferences-auto-formatting", Te3Page.PreferencesTarget(), new(1155, 714, 0, 0, 1155, 714));
            await c.Page.CancelPreferences();
        });
    }
    private static async Task CodeActionsPreferences(RecipeContext c)
    {
        await c.Unchanged(async () =>
        {
            await c.Page.OpenPreferences();
            await c.Page.SizePreferences(1155, 714);
            await c.Page.SearchPreferences("Code Actions");
            await c.Page.SelectPreferencesSection("Code Actions");
            await c.MapControls("code-actions", new(AutomationId: "DAX Editor.Code Actions"));
            await c.MapControls("preferences-tree", new(AutomationId: "treePreferences"));
            await c.Page.AssertCodeActionsState();
            await c.Capture("preferences-code-actions", Te3Page.PreferencesTarget(), new(1155, 714, 0, 0, 1155, 714));
            await c.Page.CancelPreferences();
        });
    }
    private static async Task DaxGeneralPreferences(RecipeContext c)
    {
        await c.Unchanged(async () =>
        {
            await c.Page.OpenPreferences();
            await c.Page.SizePreferences(1155, 714);
            await c.Page.SelectPreferencesFirstChild("DAX Editor", "General");
            await c.Page.AssertDaxGeneralState();
            await c.MapControls("dax-general", new(AutomationId: "DAX Editor.General"));
            await c.MapControls("preferences-tree", new(AutomationId: "treePreferences"));
            await c.Capture("preferences-dax-general", Te3Page.PreferencesTarget(), new(1155, 714, 0, 0, 1155, 714));
            await c.Page.CancelPreferences();
        });
    }
    private static async Task FileFormatsPreferences(RecipeContext c)
    {
        await c.Unchanged(async () =>
        {
            await c.Page.OpenPreferences();
            await c.Page.SizePreferences(1389, 855);
            await c.Page.SearchPreferences("File Formats");
            await c.Page.SelectPreferencesSection("File Formats");
            await c.Page.AssertFileFormatsState();
            await c.MapControls("file-formats", new(AutomationId: "File Formats.General"));
            await c.MapControls("preferences-tree", new(AutomationId: "treePreferences"));
            await c.Page.AssertFileFormatsAnnotationRows();
            await c.Capture("preferences-file-formats", Te3Page.PreferencesTarget(), new(1389, 855, 0, 0, 1389, 855));
            await c.Page.AssertFileFormatsAnnotationRows();
            await c.Page.CancelPreferences();
        });
    }
    private static async Task SaveToFolderPreferences(RecipeContext c)
    {
        await c.Unchanged(async () =>
        {
            await c.Page.OpenPreferences();
            await c.Page.SizePreferences(1155, 714);
            await c.Page.SearchPreferences("Save-to-folder");
            await c.Page.SelectPreferencesSection("Save-to-folder");
            await c.Page.Expect(new(new(AutomationId: "File Formats.Save-to-folder"), IncludeOwned: true));
            await c.Page.Expect(new(new(Name: "Serialization mode", ControlType: "ComboBox"),
                new(AutomationId: "File Formats.Save-to-folder"), true), "Database.json (default)");
            await c.MapControls("save-to-folder", new(AutomationId: "File Formats.Save-to-folder"));
            await c.Capture("preferences-save-to-folder", Te3Page.PreferencesTarget(), new(1155, 714, 0, 0, 1155, 714));
            await c.Page.OpenSerializationModes();
            await c.MapSerializationModes();
            await c.Page.CloseSerializationModes();
            await c.Page.StageCustomSerialization();
            await c.Page.StageOriginalSerializationOptions();
            await c.Page.AssertSerializationTreeState();
            await c.MapControls("save-to-folder-custom", new(AutomationId: "File Formats.Save-to-folder"));
            await c.Capture("preferences-save-to-folder-custom", Te3Page.PreferencesTarget(), new(1155, 714, 0, 0, 1155, 714));
            await c.Page.CancelPreferences();
        });
    }
    private static async Task CalculationGroupMenu(RecipeContext c)
    {
        await c.Unchanged(async () =>
        {
            await c.Page.CloseOptionalDocument("What's New");
            await c.CaptureCalculationGroupMenu();
        });
    }
    private static async Task MeasureEdit(RecipeContext c) { await Revenue(c); await c.Capture("measure-editor"); }
    private static async Task DaxQuery(RecipeContext c)
    {
        await Revenue(c); await c.Page.NewQuery(); await c.Page.RunQuery("EVALUATE { [UI Revenue] }");
        await c.Page.AssertQueryResult("61"); await c.Capture("query-result");
    }
    private static async Task ExpressionRecovery(RecipeContext c)
    {
        await Revenue(c); await c.Page.SetExpression("[Missing Measure]"); await c.Page.AssertMissingMeasureError();
        await c.Capture("expression-error");
        // SetExpression presses F5 to refresh validation before settled absence.
        await c.Page.SetExpression("[Total Amount] + 2"); await c.Page.AssertNoErrors(); await c.Page.Save();
        await c.Query("EVALUATE { [UI Revenue] }", 62); await c.Capture("expression-recovered");
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
        await Revenue(c);
        await c.Page.OpenBpa(); await c.Page.RefreshBpa(); await c.Page.ShowBpaBottom();
        await c.Page.AssertBpaViolation("fla_ UI description required (1 object)"); await c.Capture("bpa-violation");
        await c.Page.SelectObjectPath("Sales", "UI Revenue", "Measure");
        await c.Page.SetProperty("Description", "Documented test revenue"); await c.Page.Save();
        await c.Metadata("Sales", "Measure", "UI Revenue", "Description", "Documented test revenue");
        await c.Page.OpenBpa(); await c.Page.RefreshBpa(); await c.Page.ShowBpaBottom();
        await c.Page.AssertBpaClear("fla_ UI description required (1 object)"); await c.Capture("bpa-corrected");
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
        await Revenue(c); await ConfigureProperties(c); await c.Capture("property-editor");
    }
    private static async Task Preview(RecipeContext c)
    {
        await Revenue(c); await ConfigureProperties(c);
        await c.Page.SetProperty("Description", "Documented test revenue"); await c.Page.Save();
        await c.LoadScript("preview", "Model.Tables[\"Sales\"].Measures[\"UI Revenue\"].Description = \"Accepted script change\";");
        await c.Page.PreviewCSharpScript();
        await c.Page.ShowPreviewDescription("UI Revenue", "Documented test revenue", "Accepted script change");
        await c.Capture("script-preview", Te3Page.ScriptPreviewTarget());
        await c.Page.CancelScriptPreview(); await c.Page.AssertProperty("Description", "Documented test revenue");
        await c.Page.PreviewCSharpScript(); await c.Page.AcceptScriptPreview();
        await c.Page.AssertProperty("Description", "Accepted script change");
        await c.Page.SelectDocument("Expression Editor"); await c.Page.Save();
        await c.Metadata("Sales", "Measure", "UI Revenue", "Description", "Accepted script change");
        await c.Capture("script-accepted");
    }
    private static async Task PreviewPage(RecipeContext c)
    {
        await c.Page.SelectObjectPath("Sales", "Total Amount", "Measure", "Smoke tests");
        await c.Page.SetProperty("Description", "Original description"); await c.Page.Save();
        await c.LoadScript("preview-page", "Model.Tables[\"Sales\"].Measures[\"Total Amount\"].Description = \"Updated description\";");
        await c.Capture("csharp-preview-toolbar-context");
        await c.Capture("csharp-preview-command", Te3Page.RunPreviewTarget());
        await c.Page.PreviewCSharpScript();
        await c.Page.ShowPreviewDescription("Total Amount", "Original description", "Updated description");
        await c.Capture("csharp-preview-diff-component", Te3Page.ScriptPreviewTarget());
        await c.Page.CancelScriptPreview(); await c.Page.AssertProperty("Description", "Original description");
        await c.Page.PreviewCSharpScript(); await c.Page.AcceptScriptPreview();
        await c.Page.AssertProperty("Description", "Updated description");
        await c.Page.SelectDocument("Expression Editor"); await c.Page.Save();
        await c.Metadata("Sales", "Measure", "Total Amount", "Description", "Updated description");
        await c.Page.SelectObjectPath("Sales", "Total Amount", "Measure", "Smoke tests"); await c.Page.UndoModelChange();
        await c.Page.AssertProperty("Description", "Original description"); await c.Page.Save();
        await c.Metadata("Sales", "Measure", "Total Amount", "Description", "Original description");
        await c.Capture("csharp-preview-undone");
    }
    private static async Task ScalarScene(RecipeContext c)
    {
        await c.Unchanged(async () =>
        {
            await c.Page.CloseOptionalDocument("What's New");
            await c.LoadScript("C# Script 3", "\"Hello World\".Output();");
            await c.Page.RunCSharpScript();
            await c.Page.AssertScalarOutput("Hello World");
            await c.Capture("csharp-scalar-scene", scalarScene: true);
            await c.Page.CloseScriptOutput(); await c.Page.AssertOutputClosed();
            await c.Page.CloseCSharpDocument();
        });
    }
    private static async Task ScalarOutput(RecipeContext c)
    {
        await c.Unchanged(async () =>
        {
            await c.LoadScript("scalar", "\"Hello World\".Output(); \"Script resumed\".Output();");
            await c.Capture("csharp-output-scalar-context"); await c.Page.RunCSharpScript();
            await c.Page.AssertScalarOutput("Hello World");
            await c.Capture("csharp-output-scalar-dialog", Te3Page.ScriptOutputTarget());
            await c.Page.CloseScriptOutput(); await c.Page.AssertScalarOutput("Script resumed");
            await c.Page.CloseScriptOutput(); await c.Page.AssertOutputClosed();
        });
    }
    private static async Task ObjectOutput(RecipeContext c)
    {
        await c.Unchanged(async () =>
        {
            await c.LoadScript("object", "Model.Tables[\"Sales\"].Output();");
            await c.Capture("csharp-output-object-context"); await c.Page.RunCSharpScript();
            await c.Page.AssertOutputProperty("Name", "Sales");
            await c.Page.AssertOutputProperty("Description", "Three synthetic sales amounts for UI automation.");
            await c.Page.AssertOutputProperty("DAX identifier", "'Sales'");
            await c.Page.AssertOutputProperty("Object Type", "Calculated Table");
            await c.Capture("csharp-output-object-dialog", Te3Page.ScriptOutputTarget());
            await c.Page.CloseScriptOutput(); await c.Page.AssertOutputClosed();
        });
    }
    private static async Task ValuesOutput(RecipeContext c)
    {
        await c.Unchanged(async () =>
        {
            await c.LoadScript("values", "new [] { \"Alpha\", \"Beta\" }.Output();");
            await c.Capture("csharp-output-values-context"); await c.Page.RunCSharpScript(); await c.Page.AssertTwoOutputRows();
            await c.Page.AssertOutputCell("1", "Alpha"); await c.Page.AssertOutputCell("2", "Beta");
            await c.Page.AssertNoOutputObjectName("Name");
            await c.Capture("csharp-output-values-dialog", Te3Page.ScriptOutputTarget());
            await c.Page.CloseScriptOutput(); await c.Page.AssertOutputClosed();
        });
    }
    private static async Task FileLoad(RecipeContext c)
    {
        await c.Unchanged(async () =>
        {
            for (var cycle = 1; cycle <= 3; cycle++)
            {
                await c.LoadScript("hello-world", "\"Hello World\".Output();");
                await c.Capture($"file-load-{cycle}-editor"); await c.Page.RunCSharpScript();
                await c.Page.AssertScalarOutput("Hello World");
                await c.Capture($"file-load-{cycle}-output", Te3Page.ScriptOutputTarget());
                await c.Page.CloseScriptOutput(); await c.Page.AssertOutputClosed(); await c.Page.CloseCSharpDocument();
            }
        });
    }
    private static async Task AutoRollbackSource(RecipeContext c)
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "replica-inputs", "Columns to Measures.csx")).TrimEnd('\r', '\n');
        await c.Unchanged(async () =>
        {
            await c.Page.CloseOptionalDocument("What's New");
            await c.LoadScript("Columns to Measures", source);
            await c.Page.AssertAutoRollbackEnabled();
            await c.Capture("auto-rollback-source-context");
            await c.Capture("auto-rollback-control", Te3Page.AutoRollbackTarget());
            // This original documents editor state. Do not execute its model-changing script.
            await c.Page.CloseCSharpDocument();
        });
    }
}
