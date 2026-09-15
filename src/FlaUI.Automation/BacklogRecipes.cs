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
        ("L375", "TOM Explorer"),
        ("L380", "Proxy Settings")
    ];

    internal static IEnumerable<string> Items =>
        Menus.Select(m => m.Item).Concat(Dialogs.SelectMany(d => d.Items)).Concat(PreferenceSections.Select(p => p.Item));

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
