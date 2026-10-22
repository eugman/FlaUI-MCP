using System.Diagnostics;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Mcp.Core;

public sealed partial class Te3Page
{
    public static Target PreferencesTarget() =>
        new(new(Name: "Preferences", ControlType: "Window", RootOnly: true), null, true);

    internal string PreferencesHandle()
    {
        var pid = host.Sessions.GetWindowProcessId(handle);
        var windows = Win32Desktop.GetTopLevelWindows(pid).Where(w => w.Title == "Preferences").ToArray();
        if (windows.Length != 1)
        {
            throw new InvalidOperationException("Expected exactly one native Preferences window.");
        }

        return host.Sessions.RegisterNativeWindow(windows[0].Hwnd, pid);
    }

    public async Task OpenPreferences()
    {
        await Click(new(new(Name: "Tools", ControlType: "MenuItem"), new(Name: "Main menu", ControlType: "MenuBar")));
        await ClickPopupCommand(
            await Resolve(new(new(Name: "Preferences...", ControlType: "Button"), null, true)),
            "Preferences command"
        );
        var owner = host.Sessions.GetInputTarget(handle);
        reportStep?.Invoke("Wait for native Preferences window before querying its controls");
        var opened = await Poll.Until(
            () =>
            {
                owner.EnsureAlive();
                return Win32Desktop.GetTopLevelWindows(owner.ProcessId).Any(w => w.Title == "Preferences");
            },
            TimeSpan.FromSeconds(30)
        );
        if (!opened)
        {
            throw new TimeoutException("Preferences did not open");
        }

        using var process = Process.GetProcessById(owner.ProcessId);
        if (!await Task.Run(() => process.WaitForInputIdle(15000)).WaitAsync(TimeSpan.FromSeconds(16)))
        {
            throw new TimeoutException("Preferences did not finish initializing");
        }

        _ = PreferencesHandle();
    }

    public async Task CancelPreferences()
    {
        await Click(new(new(Name: "Cancel", ControlType: "Button"), PreferencesTarget().Selector, true));
        await WaitForMainWindowReady();
        await Absent(PreferencesTarget());
    }

    public async Task SearchPreferences(string text) =>
        await Fill(
            new(new(AutomationId: "searchPreferences", ControlType: "Edit"), PreferencesTarget().Selector, true),
            text
        );

    public async Task SizePreferences(int width, int height)
    {
        var reference = await Reference(PreferencesTarget());
        var window = host.Elements.InputForRef(reference);
        var area = Screen.FromHandle(window.Hwnd).WorkingArea;
        var requested = new System.Drawing.Rectangle(
            area.Left + (area.Width - width) / 2,
            area.Top + (area.Height - height) / 2,
            width,
            height
        );
        await PlaceWindow(reference, window.Hwnd, requested);
        using var process = Process.GetProcessById(window.ProcessId);
        if (!await Task.Run(() => process.WaitForInputIdle(15000)).WaitAsync(TimeSpan.FromSeconds(16)))
        {
            throw new TimeoutException("Preferences did not finish resizing");
        }

        await Task.Delay(500);
        window.EnsureAlive();
        var observed = Win32Desktop.GetWindowBounds(window.Hwnd);
        if (
            observed == null
            || observed.Value.Width != width
            || observed.Value.Height != height
            || !area.Contains(observed.Value)
        )
        {
            throw new InvalidOperationException(
                $"Preferences size differs: expected {width}x{height}, observed {observed}"
            );
        }
    }

    public async Task SelectPreferencesSection(string section)
    {
        var row = new Target(new(ControlType: "TreeItem", Value: section), new(AutomationId: "treePreferences"), true);
        await Click(row);
        var selected = await Resolve(row);
        if (
            !await Task.Run(() => selected.Patterns.SelectionItem.Pattern.IsSelected.Value)
                .WaitAsync(TimeSpan.FromSeconds(5))
        )
        {
            throw new InvalidOperationException("Preferences section is not selected: " + section);
        }

        await SearchPreferences("");
        var tree = new Target(new(AutomationId: "treePreferences"), PreferencesTarget().Selector, true);
        var visible = false;
        for (var page = 0; page < 8; page++)
        {
            visible = await Task.Run(() =>
                    query.IsPresent(
                        PreferencesHandle(),
                        row.Selector,
                        row.Within,
                        false,
                        new SearchBudget(1000, TimeSpan.FromSeconds(2))
                    )
                )
                .WaitAsync(TimeSpan.FromSeconds(5));
            if (visible)
            {
                break;
            }

            await Keys("PageDown", tree);
        }

        if (!visible)
        {
            throw new InvalidOperationException("Preferences section did not become visible after clearing search");
        }

        await Click(row);
        selected = await Resolve(row);
        if (
            !await Task.Run(() => selected.Patterns.SelectionItem.Pattern.IsSelected.Value)
                .WaitAsync(TimeSpan.FromSeconds(5))
        )
        {
            throw new InvalidOperationException("Clearing search changed the selected Preferences section");
        }
    }

    public async Task SelectPreferencesFirstChild(string parent, string child)
    {
        await SearchPreferences(parent);
        await SelectPreferencesSection(parent);
        var tree = new Target(new(AutomationId: "treePreferences"), PreferencesTarget().Selector, true);
        async Task<List<ElementInfo>> Rows()
        {
            var result = await Task.Run(() =>
                    query.Find(
                        handle,
                        new(ControlType: "TreeItem", Visible: true),
                        tree.Selector,
                        maxResults: 100,
                        includeOwned: true,
                        budget: new SearchBudget(1000, TimeSpan.FromSeconds(3))
                    )
                )
                .WaitAsync(TimeSpan.FromSeconds(5));
            if (result.Truncated || result.Unreadable != 0)
            {
                throw new InvalidOperationException("Preferences tree observation incomplete");
            }

            return result.Elements;
        }

        await Keys("Right", tree);
        var rows = await Rows();
        if (rows.SingleOrDefault(row => row.Selected == true)?.Value == parent)
        {
            await Keys("Down", tree);
            rows = await Rows();
        }

        VerifyPreferencesFirstChild(rows, parent, child);
    }

    public static void VerifyPreferencesFirstChild(IReadOnlyList<ElementInfo> rows, string parent, string child)
    {
        var parents = rows.Select((row, index) => (row, index)).Where(pair => pair.row.Value == parent).ToArray();
        var selected = rows.Select((row, index) => (row, index)).Where(pair => pair.row.Selected == true).ToArray();
        if (
            parents.Length != 1
            || selected.Length != 1
            || selected[0].row.Value != child
            || selected[0].index != parents[0].index + 1
        )
        {
            throw new InvalidOperationException($"Expected selected first child {parent} > {child}");
        }
    }

    public async Task SaveControlMap(string path, ElementSelector within)
    {
        var scoped = await Task.Run(() =>
                query.Find(
                    handle,
                    new(Visible: true),
                    within,
                    maxResults: 200,
                    includeOwned: true,
                    budget: new SearchBudget(1000, TimeSpan.FromSeconds(3))
                )
            )
            .WaitAsync(TimeSpan.FromSeconds(5));
        File.WriteAllText(path, JsonSerializer.Serialize(scoped, Indented));
    }

    public async Task AssertAutoRollbackEnabled()
    {
        var element = await Resolve(AutoRollbackTarget());
        reportStep?.Invoke("Assert Auto-rollback toggle is on (read only)");
        var enabled = await Task.Run(() =>
                element.Patterns.Toggle.IsSupported
                && element.Patterns.Toggle.Pattern.ToggleState.Value == FlaUI.Core.Definitions.ToggleState.On
            )
            .WaitAsync(TimeSpan.FromSeconds(5));
        if (!enabled)
        {
            throw new InvalidOperationException("Auto-rollback must be on for this original-image capture");
        }
    }
}
