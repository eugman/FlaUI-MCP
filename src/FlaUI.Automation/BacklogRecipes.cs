/// <summary>
/// Wave 1 backlog recipes on the offline SpaceParts fixture (automation/backlog.config.json).
/// Each checkpoint is named after its backlog item and captured clean; crops and annotations are composed later.
/// Nothing is confirmed: menus close with Escape, dialogs with CancelDialog, Preferences with Cancel.
/// </summary>
public static class BacklogRecipes
{
    // Backlog item -> main-menu path whose open popup is the screenshot.
    internal static readonly (string Item, string[] Path)[] Menus =
    [
        ("L234", ["File"]),
        ("L232", ["File", "New"]),
        ("L233", ["File", "Open"]),
        ("L473", ["View"]),
        ("L377", ["Tools"]),
        ("L257", ["Help"]),
        ("L295", ["Model"]),
        ("L457", ["Window", "Language"])
    ];

    // Backlog items -> menu path to a dialog shown in its default state. One capture can serve several items.
    internal static readonly (string[] Items, string[] Path)[] Dialogs =
    [
        (["L301", "L436"], ["File", "New", "Model..."]),
        (["L081"], ["File", "Open", "Model from DB..."]),
        (["L285"], ["Window", "Layouts..."]),
        (["L188"], ["Tools", "Manage BPA rules..."])
    ];

    // Backlog item -> Preferences section selected by searching for its name.
    internal static readonly (string Item, string Section)[] PreferenceSections =
    [
        ("L375", "TOM Explorer")
    ];

    // Backlog item -> menu path and the entry shown highlighted.
    internal static readonly (string Item, string[] Path, string Entry)[] MenuHighlights =
    [
        ("L212", ["View"], "DAX Optimizer"),
        ("L336", ["File"], "Save to Folder...")
    ];

    // Backlog item -> Preferences section whose (only) dropdown is shown open. Names unobserved; the pane map is saved first.
    internal static readonly (string Item, string Section)[] PreferenceDropdowns =
    [
        ("L380", "Proxy Settings"),
        ("L231", "Pivot Grid"),
        ("L023", "AI Provider")
    ];

    // Dummy hosts only: nothing connects before OK, which is never pressed.
    private const string PowerBiServer = "powerbi://api.powerbi.com/v1.0/myorg/SpaceParts.invalid";
    private const string DummyServer = "ssas.example.invalid";
    internal static readonly string[] LoadFromDbItems = ["L072", "L073", "L302"];

    internal static IEnumerable<string> Items =>
        Menus.Select(m => m.Item).Concat(Dialogs.SelectMany(d => d.Items)).Concat(PreferenceSections.Select(p => p.Item))
            .Concat(MenuHighlights.Select(m => m.Item)).Concat(LoadFromDbItems)
            .Concat(PreferenceDropdowns.Select(p => p.Item));

    public static async Task MenuHighlightRecipes(RecipeContext c)
    {
        await c.Unchanged(async () =>
        {
            foreach (var (item, path, entry) in MenuHighlights)
            {
                await c.Page.OpenMenuPath(path);
                await c.Page.HighlightMenuItem(entry);
                await c.CaptureWithPopups(item);
                foreach (var _ in path) await c.Page.PressKeys("Escape");
                await Task.Delay(300);
            }
        });
    }

    public static async Task LoadFromDbRecipes(RecipeContext c)
    {
        string[] path = ["File", "Open", "Model from DB..."];
        await c.Unchanged(async () =>
        {
            var dialog = await c.Page.OpenDialog(path);
            await c.Page.MapDialog(dialog, c.OutputPath("load-from-db.uia.json"));
            await c.Page.StageField(dialog, "Server:", "ComboBox", PowerBiServer);
            await c.CaptureDialog("L072", dialog);
            await c.Page.CancelDialog(dialog);

            dialog = await c.Page.OpenDialog(path);
            await c.Page.StageField(dialog, "Server:", "ComboBox", PowerBiServer);
            await c.Page.ChooseOption(dialog, "Microsoft Entra MFA");
            await c.CaptureDialog("L073", dialog);
            await c.Page.CancelDialog(dialog);

            dialog = await c.Page.OpenDialog(path);
            await c.Page.StageField(dialog, "Server:", "ComboBox", DummyServer);
            await c.Page.OpenDropdown(dialog, "Mode:");
            // The dropdown is its own popup window; a plain capture of the main window includes it.
            await c.CaptureWithPopups("L302");
            await c.Page.CancelDialog(dialog);
        });
    }

    // Map only: the Compatibility Level list decides how L054 and R035 are written.
    public static async Task NewModelOptions(RecipeContext c)
    {
        await c.Unchanged(async () =>
        {
            var dialog = await c.Page.OpenDialog("File", "New", "Model...");
            await c.Page.OpenDropdown(dialog, "Compatibility Level");
            await c.Page.MapPopups(c.OutputPath("new-model-compatibility-levels.uia.json"));
            await c.Page.SaveImage(c.Page.MainHandle, c.OutputPath("new-model-compatibility-levels.png"), background: false);
            await c.Page.CancelDialog(dialog);
        });
    }

    public static async Task PreferenceDropdownRecipes(RecipeContext c)
    {
        await c.Unchanged(async () =>
        {
            foreach (var (item, section) in PreferenceDropdowns)
            {
                await c.Page.OpenPreferences();
                await c.Page.SizePreferences(1155, 714);
                await c.Page.SearchPreferences(section);
                await c.Page.SelectPreferencesSection(section);
                await c.MapControls($"{item}-preferences", Te3Page.PreferencesTarget().Selector);
                await c.Page.OpenPreferencesDropdown();
                await c.CapturePreferencesWithPopups(item);
                await c.Page.SendKeysToPreferences("Escape");
                await c.Page.CancelPreferences();
            }
        });
    }

    public static async Task MenuRecipes(RecipeContext c)
    {
        await c.Unchanged(async () =>
        {
            foreach (var (item, path) in Menus)
            {
                await c.Page.OpenMenuPath(path);
                await c.CaptureWithPopups(item);
                foreach (var _ in path) await c.Page.PressKeys("Escape");
                await Task.Delay(300);
            }
        });
    }

    public static async Task DialogRecipes(RecipeContext c)
    {
        await c.Unchanged(async () =>
        {
            foreach (var (items, path) in Dialogs)
            {
                var dialog = await c.Page.OpenDialog(path);
                foreach (var item in items) await c.CaptureDialog(item, dialog);
                await c.Page.CancelDialog(dialog);
            }
        });
    }

    public static async Task PreferenceRecipes(RecipeContext c)
    {
        await c.Unchanged(async () =>
        {
            foreach (var (item, section) in PreferenceSections)
            {
                await c.Page.OpenPreferences();
                await c.Page.SizePreferences(1155, 714);
                await c.Page.SearchPreferences(section);
                await c.Page.SelectPreferencesSection(section);
                await c.Capture(item, Te3Page.PreferencesTarget(), new(1155, 714, 0, 0, 1155, 714));
                await c.Page.CancelPreferences();
            }
        });
    }
}
