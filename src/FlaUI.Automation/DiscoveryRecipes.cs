using System.Text.Json;
using System.Text.RegularExpressions;

/// <summary>
/// Capture-only probes that save UIA maps for the backlog helpers.
/// Nothing is confirmed: no OK, Connect, Apply or model edits; every popup and dialog is closed with Escape.
/// A failed probe is recorded in discovery-errors.json; the run stops if TE3 is not back to its main window.
/// </summary>
public static partial class DiscoveryRecipes
{
    // Round 1 (2026-09-15): menus, folder context menus, first dialogs and Preferences searches.
    private static readonly (string Menu, string[] Items)[] Dialogs =
    [
        ("File", ["New", "Model..."]),
        ("Window", ["Layouts..."]),
        ("Window", ["Windows..."]),
        ("Tools", ["Manage BPA rules..."]),
        ("Help", ["About Tabular Editor"])
    ];
    private static readonly string[] TreeNodes = ["Roles", "Shared Expressions", "Data Sources", "Relationships"];
    private static readonly string[] PreferenceSearches = ["Compiler", "Features", "Proxy", "TOM Explorer", "AI Provider", "Keyboard"];

    // Round 2: exercises the recipe helpers on the gaps round 1 left.
    private static readonly string[][] Submenus = [["File", "Open"], ["Window", "Language"], ["Window", "Theme"], ["Edit", "Code Assist"], ["View", "Toolbars"]];
    private static readonly string[][] DialogPaths =
    [
        ["File", "Open", "Model from DB..."],
        ["Window", "Layouts..."],
        ["Window", "Windows..."],
        ["Tools", "Manage BPA rules..."]
    ];

    public static async Task Backlog(RecipeContext c)
    {
        var page = c.Page;
        var errors = new List<string>();
        var probe = Prober(page, errors);

        await c.Unchanged(async () =>
        {
            string[] menus = [];
            await probe("menu bar", async () =>
            {
                await c.MapControls("main-menu", new(Name: "Main menu", ControlType: "MenuBar"));
                menus = await page.MenuBarItems();
            });
            foreach (var menu in menus)
                await probe("menu " + menu, async () =>
                {
                    await page.OpenMenu(menu);
                    await page.MapPopups(c.OutputPath($"menu-{Slug(menu)}.uia.json"));
                    await page.SaveImage(page.MainHandle, c.OutputPath($"menu-{Slug(menu)}.png"), background: false);
                    await page.PressKeys("Escape");
                    await Task.Delay(300);
                });

            await probe("File > New submenu", async () =>
            {
                await page.OpenMenu("File");
                await page.ClickPopupItem("New");
                await Task.Delay(500);
                await page.MapPopups(c.OutputPath("menu-file-new.uia.json"));
                await page.PressKeys("Escape");
                await page.PressKeys("Escape");
            });

            await probe("TOM tree", async () =>
            {
                await page.OpenTom();
                await c.MapControls("tom-tree", new(AutomationId: "treeList"));
            });
            foreach (var node in TreeNodes)
                await probe("context menu " + node, async () =>
                {
                    await page.SelectObject(node);
                    await page.PressKeys("Shift+F10");
                    await Task.Delay(500);
                    await page.MapPopups(c.OutputPath($"context-{Slug(node)}.uia.json"));
                    await page.PressKeys("Escape");
                });
            await probe("expression editor", () => c.MapControls("expression-editor", new(AutomationId: "QuickEditorView")));
            await probe("messages", () => c.MapControls("messages", new(AutomationId: "MessagesView")));

            foreach (var (menu, items) in Dialogs)
                await probe($"dialog {menu} > {string.Join(" > ", items)}", async () =>
                {
                    var before = page.TitledWindows().Select(w => w.Hwnd).ToArray();
                    await page.OpenMenu(menu);
                    foreach (var item in items) { await page.ClickPopupItem(item); await Task.Delay(400); }
                    var (dialog, window) = await page.WaitForNewWindow(before, TimeSpan.FromSeconds(15));
                    var name = Slug(items[^1]);
                    await page.MapWindow(dialog, c.OutputPath($"dialog-{name}.uia.json"));
                    await page.SaveImage(dialog, c.OutputPath($"dialog-{name}.png"), background: true);
                    await page.SendKeysTo(dialog, "Escape");
                    await page.WaitForWindowClosed(window.Hwnd, TimeSpan.FromSeconds(10));
                });

            await probe("preferences searches", async () =>
            {
                await page.OpenPreferences();
                foreach (var term in PreferenceSearches)
                {
                    await page.SearchPreferences(term);
                    await Task.Delay(500);
                    await c.MapControls($"preferences-{Slug(term)}", new(AutomationId: "treePreferences"));
                }
                await page.CancelPreferences();
            });
        });

        WriteErrors(c, errors);
    }

    public static async Task Backlog2(RecipeContext c)
    {
        var page = c.Page;
        var errors = new List<string>();
        var probe = Prober(page, errors);

        await c.Unchanged(async () =>
        {
            foreach (var path in Submenus)
                await probe("submenu " + string.Join(" > ", path), async () =>
                {
                    await page.OpenMenuPath(path);
                    var name = Slug(string.Join(" ", path));
                    await page.MapPopups(c.OutputPath($"menu-{name}.uia.json"));
                    await page.SaveImage(page.MainHandle, c.OutputPath($"menu-{name}.png"), background: false);
                    for (var i = 0; i < path.Length; i++) await page.PressKeys("Escape");
                });

            await probe("TOM tab", page.OpenTom);
            await probe("Roles > Create", async () =>
            {
                await page.OpenContextMenu(["Roles"], "Create");
                await page.MapPopups(c.OutputPath("context-roles-create.uia.json"));
                await page.SaveImage(page.MainHandle, c.OutputPath("context-roles-create.png"), background: false);
                await page.PressKeys("Escape");
                await page.PressKeys("Escape");
            });
            // Two ways to reach a table row that may be scrolled out of view; the errors file says which works.
            await probe("Invoices by node path", async () =>
            {
                await page.OpenContextMenu(["Tables", "Invoices"]);
                await page.MapPopups(c.OutputPath("context-invoices.uia.json"));
                await page.SaveImage(page.MainHandle, c.OutputPath("context-invoices.png"), background: false);
                await page.PressKeys("Escape");
            });
            await probe("Invoices by search", async () =>
            {
                await page.SelectBySearch("Invoices");
                await c.MapControls("properties-invoices", new(AutomationId: "PropertyGridView"));
                await page.ExpandSelected();
                await c.MapControls("tom-tree-invoices", new(AutomationId: "treeList"));
            });
            await probe("Roles expanded", async () =>
            {
                await page.SelectNodePath("Roles");
                await page.ExpandSelected();
                await c.MapControls("tom-tree-roles", new(AutomationId: "treeList"));
            });

            foreach (var path in DialogPaths)
                await probe("dialog " + string.Join(" > ", path), async () =>
                {
                    var dialog = await page.OpenDialog(path);
                    var name = Slug(path[^1]);
                    await page.MapDialog(dialog, c.OutputPath($"dialog-{name}.uia.json"));
                    await page.SaveImage(dialog.Handle, c.OutputPath($"dialog-{name}.png"), background: true);
                    await page.CancelDialog(dialog);
                });
        });

        WriteErrors(c, errors);
    }

    private static Func<string, Func<Task>, Task> Prober(Te3Page page, List<string> errors)
    {
        var main = page.TitledWindows().Select(w => w.Hwnd).ToArray();
        return async (name, action) =>
        {
            try { await action(); }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                errors.Add($"{name}: {error.Message}");
                // Escape out of whatever the probe left open; never confirm anything.
                foreach (var extra in page.TitledWindows().Where(w => !main.Contains(w.Hwnd)))
                    try { await page.SendKeysTo(page.Register(extra), "Escape"); } catch { }
                try { await page.PressKeys("Escape"); await page.PressKeys("Escape"); } catch { }
                await Task.Delay(500);
                if (page.TitledWindows().Any(w => !main.Contains(w.Hwnd)))
                    throw new InvalidOperationException($"Probe '{name}' left a window open; stopping discovery", error);
            }
        };
    }

    private static void WriteErrors(RecipeContext c, List<string> errors) =>
        File.WriteAllText(c.OutputPath("discovery-errors.json"), JsonSerializer.Serialize(errors, new JsonSerializerOptions { WriteIndented = true }));

    internal static string Slug(string text) => NonWord().Replace(text.ToLowerInvariant(), "-").Trim('-');

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonWord();
}
