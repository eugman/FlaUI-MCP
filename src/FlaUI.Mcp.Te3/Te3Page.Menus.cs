using System.Diagnostics;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Mcp.Core;

public sealed partial class Te3Page
{
    public async Task DefaultLayout()
    {
        await Click(WindowMenuTarget());
        AutomationElement? element = null;
        var opening = Stopwatch.StartNew();
        while (element == null && opening.Elapsed < TimeSpan.FromSeconds(5))
        {
            try
            {
                element = await Resolve(DefaultLayoutTarget());
            }
            catch (InvalidOperationException)
            {
                await Task.Delay(100);
            }
        }

        if (element == null)
        {
            throw new TimeoutException("Window menu did not expose Default layout");
        }

        var popup = await MenuPopup(element);
        // TE3's non-activating menu is a same-process window without a native owner.
        // Keep the main window foreground and require hit testing against this exact popup.
        reportStep?.Invoke("Apply default layout");
        await ClickInPopup("Default layout", element, popup);
    }

    // TE3 menu commands are clicked physically inside their own popup window; UIA Invoke can stay pending.
    private async Task ClickPopupCommand(AutomationElement command, string label)
    {
        var popup = await MenuPopup(command);
        reportStep?.Invoke("Click " + label);
        await ClickInPopup(label, command, popup);
    }

    // Discovery helpers: capture-only probes that record locators before recipes are written.
    public string MainHandle => handle;

    public async Task<string[]> MenuBarItems()
    {
        var found = await Task.Run(() =>
                query.Find(
                    handle,
                    new(ControlType: "MenuItem"),
                    new(Name: "Main menu", ControlType: "MenuBar"),
                    maxResults: 30,
                    budget: new SearchBudget(1000, TimeSpan.FromSeconds(3))
                )
            )
            .WaitAsync(TimeSpan.FromSeconds(5));
        return found.Elements.Select(e => e.Name).Where(name => !string.IsNullOrEmpty(name)).Distinct().ToArray();
    }

    public async Task OpenMenu(string menu)
    {
        MarkPopups();
        await Click(new(new(Name: menu, ControlType: "MenuItem"), new(Name: "Main menu", ControlType: "MenuBar")));
        await Task.Delay(500);
    }

    public async Task ClickPopupItem(string name) =>
        await ClickPopupCommand(await Resolve(new(new(Name: name), null, true)), name);

    public async Task MapPopups(string path)
    {
        var owner = host.Sessions.GetInputTarget(handle);
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(await ObservePopups(owner.ProcessId, maxWindows: null), Indented)
        );
    }

    public Task PressKeys(string chord) => Keys(chord);

    public string Register(Win32WindowInfo window) => host.Sessions.RegisterNativeWindow(window.Hwnd, window.ProcessId);

    public Win32WindowInfo[] TitledWindows() =>
        Win32Desktop
            .GetTopLevelWindows(host.Sessions.GetWindowProcessId(handle))
            .Where(w => !w.IsCloaked && w.Title.Length > 0)
            .ToArray();

    public async Task<(string Handle, Win32WindowInfo Window)> WaitForNewWindow(
        IReadOnlyCollection<nint> before,
        TimeSpan timeout
    )
    {
        Win32WindowInfo? window = null;
        var main = host.Sessions.GetInputTarget(handle).Hwnd;
        bool Opened()
        {
            window = DialogWindows.NewDialog(ProcessWindows(), before, main);
            return window != null;
        }

        if (!await Poll.Until(Opened, timeout))
        {
            throw new TimeoutException(
                "No new TE3 dialog opened (owned by its main window, or the only new dialog-class window)"
            );
        }

        return (host.Sessions.RegisterNativeWindow(window!.Hwnd, window.ProcessId), window);
    }

    public async Task WaitForWindowClosed(nint hwnd, TimeSpan timeout)
    {
        if (!await Poll.Until(() => DialogWindows.State(ProcessWindows(), hwnd) == "closed", timeout))
        {
            throw new TimeoutException(
                DialogWindows.State(ProcessWindows(), hwnd) == "cloaked"
                    ? "Window was cloaked, not closed: it is still there, hidden"
                    : "Window did not close"
            );
        }
    }

    private IReadOnlyList<Win32WindowInfo> ProcessWindows() =>
        Win32Desktop.GetTopLevelWindows(host.Sessions.GetWindowProcessId(handle));

    public async Task MapWindow(string windowHandle, string path)
    {
        var map = await Task.Run(() =>
                query.Find(
                    windowHandle,
                    new(Visible: true),
                    maxResults: 300,
                    budget: new SearchBudget(2000, TimeSpan.FromSeconds(4))
                )
            )
            .WaitAsync(TimeSpan.FromSeconds(6));
        File.WriteAllText(path, JsonSerializer.Serialize(map, Indented));
    }

    public Task SaveImage(string windowHandle, string path, bool background) =>
        invoke(
            "windows_screenshot",
            new
            {
                handle = windowHandle,
                savePath = path,
                includeImage = false,
                background,
            }
        );

    public Task SendKeysTo(string windowHandle, string chord) =>
        invoke("windows_send_keys", new { handle = windowHandle, chord });

    // Recipe helpers built from the 2026-09-15 discovery maps (UI-MAP.md).

    /// <summary>
    /// Opens a main-menu path such as ("File", "Open", "Model from DB..."). Each entry is searched only in the popup the
    /// previous step opened and clicked physically, so names shared with toolbars or other menus cannot match.
    /// </summary>
    public async Task OpenMenuPath(params string[] path)
    {
        if (path.Length == 0)
        {
            throw new ArgumentException("Menu path required");
        }

        var before = WindowHandles();
        await OpenMenu(path[0]);
        await ClickThroughPopups(before, path.Skip(1));
    }

    /// <summary>
    /// Selects a TOM Explorer row by the values along its path, e.g. ("Tables", "Invoices"). The tree is collapsed first so
    /// top-level folders are in view; each parent is expanded and its child is found with the keyboard, failing rather
    /// than selecting a same-named row outside the parent.
    /// </summary>
    public async Task SelectNodePath(params string[] path)
    {
        if (path.Length == 0)
        {
            throw new ArgumentException("Node path required");
        }

        await CollapseObjectTree();
        await SelectFirstRow(path[0]);
        var cursor = new TomCursor(this);
        foreach (var child in path.Skip(1))
        {
            await SelectChild(cursor, child);
        }
    }

    // Expanding the selected row inserts its children between it and the row below it, so reaching that row
    // means the child is not there. Down then Up reads it first; at the end of the tree there is none.
    private async Task SelectChild(TomCursor cursor, string child)
    {
        var parent = await cursor.Current();
        var after = await PathWalker.RowAfter(cursor, parent);
        if (after != null && await cursor.Up() != parent)
        {
            throw new InvalidOperationException(
                $"Could not return to TOM Explorer row '{parent.Value}' after reading the row below it."
            );
        }

        await ExpandSelected();
        reportStep?.Invoke($"Select TOM Explorer row {child} under {parent.Value}");
        var walk = await PathWalker.FindRow(cursor, child, after, skipCurrent: true);
        if (!walk.Found)
        {
            throw new InvalidOperationException($"No TOM Explorer row {child} under {parent.Value}: {walk.Reason}");
        }
    }

    // Setting the search box value does not apply TE3's filter; it has to be typed.
    public async Task SearchTom(string text)
    {
        await ResetSearch();
        var reference = await Reference(SearchTarget());
        reportStep?.Invoke($"Type TOM Explorer search {text}");
        await invoke("windows_type", new { @ref = reference, text });
        await Task.Delay(800);
    }

    /// <summary>
    /// Selects a TOM Explorer row by filtering with the search box, then clears the search. A filter can show a table and
    /// its partition with the same value; the table is the first row in tree order.
    /// </summary>
    public async Task SelectBySearch(string name)
    {
        await SearchTom(name);
        await SelectFirstRow(name);
        await ResetSearch();
    }

    // The first row with the value in tree order, selected with the keyboard: only the selected row is trusted, and it
    // is always in view. A breadth-first search for the row could pick a hidden or deeper match instead.
    private async Task SelectFirstRow(string value)
    {
        reportStep?.Invoke($"Select first TOM Explorer row with value {value}");
        await SelectFirstTomRow();
        var walk = await PathWalker.FindRow(new TomCursor(this), value);
        if (!walk.Found)
        {
            throw new InvalidOperationException($"No TOM Explorer row with value {value}: {walk.Reason}");
        }
    }

    /// <summary>Opens the context menu of a TOM Explorer row with Shift+F10, then clicks any cascade items such as "Create".</summary>
    public async Task OpenContextMenu(string[] nodePath, params string[] items)
    {
        await SelectNodePath(nodePath);
        var before = WindowHandles();
        await Keys("Shift+F10");
        await ClickThroughPopups(before, items);
    }

    /// <summary>
    /// Highlights an entry of the open menu with Home and Down, checking keyboard focus at each step.
    /// Hovering would also highlight it but raises a tooltip over the menu.
    /// </summary>
    public async Task HighlightMenuItem(string name, int maxSteps = 30)
    {
        await Keys("Home");
        for (var step = 0; step <= maxSteps; step++)
        {
            if (step > 0)
            {
                await Keys("Down");
            }

            var matches = await Task.Run(() =>
                    query.Find(
                        handle,
                        new(Name: name, Visible: true),
                        maxResults: 10,
                        includeOwned: true,
                        budget: new SearchBudget(1000, TimeSpan.FromSeconds(2))
                    )
                )
                .WaitAsync(TimeSpan.FromSeconds(5));
            if (matches.Elements.Any(element => element.KeyboardFocus == true))
            {
                return;
            }
        }

        throw new InvalidOperationException($"Menu entry {name} was not highlighted within {maxSteps} steps");
    }
}
