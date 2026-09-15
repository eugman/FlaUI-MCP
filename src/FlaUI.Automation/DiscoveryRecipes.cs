using System.Text.Json;
using System.Text.RegularExpressions;

/// <summary>
/// Capture-only probes that save UIA maps for the backlog helpers (plan phase 2).
/// Nothing is confirmed: no OK, Connect, Apply or model edits; every popup and dialog is closed with Escape.
/// A failed probe is recorded in discovery-errors.json; the run stops if TE3 is not back to its main window.
/// </summary>
public static partial class DiscoveryRecipes
{
    // Menu path to a command that opens a dialog, with names observed in the 2026-09-15 discovery run (UI-MAP.md).
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

    public static async Task Backlog(RecipeContext c)
    {
        var page = c.Page;
        var errors = new List<string>();
        var main = page.TitledWindows().Select(w => w.Hwnd).ToArray();

        async Task Probe(string name, Func<Task> action)
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
        }

        await c.Unchanged(async () =>
        {
            string[] menus = [];
            await Probe("menu bar", async () =>
            {
                await c.MapControls("main-menu", new(Name: "Main menu", ControlType: "MenuBar"));
                menus = await page.MenuBarItems();
            });
            foreach (var menu in menus)
                await Probe("menu " + menu, async () =>
                {
                    await page.OpenMenu(menu);
                    await page.MapPopups(c.OutputPath($"menu-{Slug(menu)}.uia.json"));
                    await page.SaveImage(page.MainHandle, c.OutputPath($"menu-{Slug(menu)}.png"), background: false);
                    await page.PressKeys("Escape");
                    await Task.Delay(300);
                });

            await Probe("File > New submenu", async () =>
            {
                await page.OpenMenu("File");
                await page.ClickPopupItem("New");
                await Task.Delay(500);
                await page.MapPopups(c.OutputPath("menu-file-new.uia.json"));
                await page.PressKeys("Escape");
                await page.PressKeys("Escape");
            });

            await Probe("TOM tree", async () =>
            {
                await page.OpenTom();
                await c.MapControls("tom-tree", new(AutomationId: "treeList"));
            });
            foreach (var node in TreeNodes)
                await Probe("context menu " + node, async () =>
                {
                    await page.SelectObject(node);
                    await page.PressKeys("Shift+F10");
                    await Task.Delay(500);
                    await page.MapPopups(c.OutputPath($"context-{Slug(node)}.uia.json"));
                    await page.PressKeys("Escape");
                });
            await Probe("Roles > Create cascade", async () =>
            {
                await page.SelectObject("Roles");
                await page.PressKeys("Shift+F10");
                await Task.Delay(500);
                await page.ClickPopupItem("Create");
                await Task.Delay(500);
                await page.MapPopups(c.OutputPath("context-roles-create.uia.json"));
                await page.PressKeys("Escape");
                await page.PressKeys("Escape");
            });
            // Tables sit under the collapsed Tables folder on this fixture.
            await Probe("table context menu and properties", async () =>
            {
                await page.SelectObject("Tables");
                await page.ExpandSelected();
                await page.SelectObject("Invoices");
                await page.PressKeys("Shift+F10");
                await Task.Delay(500);
                await page.MapPopups(c.OutputPath("context-invoices.uia.json"));
                await page.PressKeys("Escape");
                await c.MapControls("properties-invoices", new(AutomationId: "PropertyGridView"));
            });
            await Probe("expression editor", () => c.MapControls("expression-editor", new(AutomationId: "QuickEditorView")));
            await Probe("messages", () => c.MapControls("messages", new(AutomationId: "MessagesView")));

            foreach (var (menu, items) in Dialogs)
                await Probe($"dialog {menu} > {string.Join(" > ", items)}", async () =>
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

            await Probe("preferences searches", async () =>
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

        File.WriteAllText(c.OutputPath("discovery-errors.json"), JsonSerializer.Serialize(errors, new JsonSerializerOptions { WriteIndented = true }));
    }

    internal static string Slug(string text) => NonWord().Replace(text.ToLowerInvariant(), "-").Trim('-');

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonWord();
}
