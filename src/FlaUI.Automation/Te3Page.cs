using System.Diagnostics;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using PlaywrightWindows.Mcp.Core;

/// <summary>Typed TE3 interactions. Locators come from successful 3.26.3 runs.</summary>
public sealed class Te3Page(AutomationHost host, string handle, Func<string, object, Task> invoke, Action<string>? reportStep = null)
{
    private readonly ElementQuery query = new(host.Sessions, host.Elements, host.Pending);
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
    public CaptureEnvironment ObserveCaptureEnvironment()
        => CaptureEnvironment.Observe(host.Sessions.GetInputTarget(handle).Hwnd);
    public sealed record Target(ElementSelector Selector, ElementSelector? Within = null, bool IncludeOwned = false);
    public static Target ScriptOutputTarget() => new(new(AutomationId: "ScriptOutputForm", RootOnly: true), null, true);
    public static Target RunPreviewTarget() => new(new(Name: "Run with preview", ControlType: "Button"), null, false);
    public static Target ScriptPreviewTarget() => new(new(AutomationId: "ScriptPreviewDialog", RootOnly: true), null, true);
    public static Target OpenFileDialogTarget() => new(new(Name: "Open File", ControlType: "Window", RootOnly: true), null, true);
    public static Target OpenFileNameTarget() => new(new(AutomationId: "1148", ControlType: "Edit"), new(Name: "Open File", ControlType: "Window", RootOnly: true), true);
    public static Target CsharpTextTarget() => new(new(AutomationId: "cSharpEditor1"), null, false);
    public static Target CsharpInputTarget() => new(new(AutomationId: "daxscilla"), new(AutomationId: "cSharpEditor1"), false);
    public static Target ScriptOutputRowsTarget() => new(new(ControlType: "ListItem"), new(AutomationId: "ScriptOutputForm", RootOnly: true), true);
    public static Target ScriptOutputCellTarget(string row) => new(new(Name: $"Object row {row}", ControlType: "DataItem", Visible: true), new(AutomationId: "ScriptOutputForm", RootOnly: true), true);
    public static Target ScriptOutputPropertyTarget(string property) => new(new(Name: $"{property}", ControlType: "DataItem", Visible: true), new(AutomationId: "ScriptOutputForm", RootOnly: true), true);
    public static Target ScriptOutputTextTarget() => new(new(AutomationId: "dataTextBox", ControlType: "Edit"), new(AutomationId: "ScriptOutputForm", RootOnly: true), true);
    public static Target ScriptOutputCloseTarget() => new(new(AutomationId: "btnClose", ControlType: "Button"), new(AutomationId: "ScriptOutputForm", RootOnly: true), true);
    public static Target TomExplorerTarget() => new(new(AutomationId: "treeList"), null, false);
    public static Target PreviewSalesRowTarget() => new(new(ControlType: "TreeItem", Value: "Sales;;;Sales;", Visible: true), new(AutomationId: "diffTreeList"), true);
    public static Target PreviewTreeTarget() => new(new(AutomationId: "diffTreeList"), new(AutomationId: "ScriptPreviewDialog", RootOnly: true), true);
    public static Target PreviewMeasuresRowTarget() => new(new(ControlType: "TreeItem", Value: "measures;;;measures;", Visible: true), new(AutomationId: "diffTreeList"), true);
    public static Target PreviewMeasureRowTarget(string measure) => new(new(ControlType: "TreeItem", Value: $"{measure};;;{measure};", Visible: true), new(AutomationId: "diffTreeList"), true);
    public static Target PreviewDescriptionRowTarget(string before, string after) => new(new(ControlType: "TreeItem", Value: $"description;{before};;description;{after}", Visible: true), new(AutomationId: "diffTreeList"), true);
    public static Target DocumentTabTarget(string tab) => new(new(ControlType: "TabItem", Name: $"{tab}"), null, false);
    public static Target BpaRefreshTarget() => new(new(ControlType: "Button", Name: "Refresh"), new(AutomationId: "BestPracticeAnalyzerView"), false);
    public static Target BpaTreeTarget() => new(new(AutomationId: "treeBpa"), null, false);
    public static Target BpaRuleTarget(string rule) => new(new(ControlType: "DataItem", Value: $"{rule}"), new(AutomationId: "treeBpa"), false);
    public static Target CollapseTomTarget() => new(new(Name: "Collapse all", ControlType: "Button"), new(AutomationId: "TabularExplorerView"), false);
    public static Target TomTabTarget() => new(new(Name: "TOM Explorer", ControlType: "TabItem"), null, false);
    public static Target PropertyTarget(string property) => new(new(Name: $"{property}", ControlType: "DataItem"), new(AutomationId: "PropertyGridView"), false);
    public static Target PropertyInputTarget() => new(new(Name: "Editing control", ControlType: "Edit", Pattern: "Value"), new(AutomationId: "PropertyGridView"), false);
    public static Target PreviewAcceptTarget() => new(new(AutomationId: "okButton"), new(AutomationId: "ScriptPreviewDialog", RootOnly: true), true);
    public static Target PreviewRevertTarget() => new(new(AutomationId: "revertButton"), new(AutomationId: "ScriptPreviewDialog", RootOnly: true), true);
    public static Target RunScriptTarget() => new(new(Name: "Run script", ControlType: "Button"), null, false);
    public static Target BpaTabTarget() => new(new(Name: "Best Practice Analyzer", ControlType: "TabItem"), null, false);
    public static Target SearchTarget() => new(new(AutomationId: "searchControl1"), new(AutomationId: "TabularExplorerView"), false);
    public static Target ObjectTarget(string objectName) => new(new(ControlType: "DataItem", Value: $"{objectName}"), new(AutomationId: "treeList"), false);
    public static Target ExpressionTabTarget() => new(new(Name: "Expression Editor", ControlType: "TabItem"), null, false);
    public static Target ExpressionInputTarget() => new(new(AutomationId: "daxscilla"), new(AutomationId: "QuickEditorView"), false);
    public static Target ExpressionTarget() => new(new(AutomationId: "DaxEditor"), new(AutomationId: "QuickEditorView"), false);
    public static Target MissingMeasureDiagnosticTarget() => new(new(ControlType: "DataItem", Value: "The value for [Missing Measure] cannot be determined. Either [Missing Measure] doesn't exist, or there is no current row for a column named [Missing Measure]."), new(AutomationId: "MessagesView"), false);
    public static Target ErrorsTarget() => new(new(ControlType: "DataItem", Value: "Error"), new(AutomationId: "MessagesView"), false);
    public static Target NewMeasureNameTarget() => new(new(ControlType: "Edit", Name: "Editing control", Value: "New Measure"), new(AutomationId: "treeList"), false);
    public static Target NewQueryTarget() => new(new(Name: "New DAX Query", ControlType: "Button"), null, false);
    public static Target QueryInputTarget() => new(new(AutomationId: "daxscilla"), new(AutomationId: "daxEditor"), false);
    public static Target QueryTextTarget() => new(new(AutomationId: "daxEditor"), null, false);
    public static Target QueryResultTarget() => new(new(Name: "[Value] row 1", ControlType: "DataItem"), null, false);
    public static Target WindowMenuTarget() => new(new(Name: "Window", ControlType: "MenuItem"), new(Name: "Main menu", ControlType: "MenuBar"), false);
    public static Target DefaultLayoutTarget() => new(new(Name: "Default layout", ControlType: "Button"), null, true);
    public static Target AutoRollbackTarget() => new(new(Name: "Auto-rollback", ControlType: "CheckBox"));
    public static Target PreferencesTarget() => new(new(Name: "Preferences", ControlType: "Window", RootOnly: true), null, true);
    internal string PreferencesHandle()
    {
        var pid = host.Sessions.GetWindowProcessId(handle);
        var windows = Win32Desktop.GetTopLevelWindows(pid).Where(w => w.Title == "Preferences").ToArray();
        if (windows.Length != 1) throw new InvalidOperationException("Expected exactly one native Preferences window.");
        return host.Sessions.RegisterNativeWindow(windows[0].Hwnd, pid);
    }

    internal static bool IsPreferencesLookup(Target target) =>
        target.Selector == PreferencesTarget().Selector || target.Within == PreferencesTarget().Selector ||
        target.Selector.AutomationId == "treePreferences" || target.Within?.AutomationId == "treePreferences" ||
        target.Selector.AutomationId?.StartsWith("DAX Editor.", StringComparison.Ordinal) == true ||
        target.Within?.AutomationId?.StartsWith("DAX Editor.", StringComparison.Ordinal) == true;
    public async Task OpenPreferences()
    {
        await Click(new(new(Name: "Tools", ControlType: "MenuItem"), new(Name: "Main menu", ControlType: "MenuBar")));
        var command = await Resolve(new(new(Name: "Preferences...", ControlType: "Button"), null, true));
        var popup = await MenuPopup(command);
        var owner = host.Sessions.GetInputTarget(handle);
        var point = await Task.Run(command.GetClickablePoint).WaitAsync(TimeSpan.FromSeconds(5));
        using (var input = new GuardedInput(owner with { HitHwnd = popup }))
        {
            if (Win32Desktop.WindowAt(point) != popup) throw new InvalidOperationException("Preferences command is obscured");
            reportStep?.Invoke("Click Preferences command");
            input.Send(() => FlaUI.Core.Input.Mouse.Click(point));
        }
        reportStep?.Invoke("Wait for native Preferences window before querying its controls");
        var opening = Stopwatch.StartNew();
        while (!Win32Desktop.GetTopLevelWindows(owner.ProcessId).Any(w => w.Title == "Preferences"))
        {
            owner.EnsureAlive();
            if (opening.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException("Preferences did not open");
            await Task.Delay(100);
        }
        using var process = Process.GetProcessById(owner.ProcessId);
        if (!await Task.Run(() => process.WaitForInputIdle(15000)).WaitAsync(TimeSpan.FromSeconds(16)))
            throw new TimeoutException("Preferences did not finish initializing");
        await Expect(PreferencesTarget());
    }
    public async Task CancelPreferences()
    {
        await Click(new(new(Name: "Cancel", ControlType: "Button"), PreferencesTarget().Selector, true));
        await WaitForMainWindowReady();
        await Absent(PreferencesTarget());
    }
    public async Task SearchPreferences(string text)
        => await Fill(new(new(AutomationId: "searchPreferences", ControlType: "Edit"), PreferencesTarget().Selector, true), text);
    public async Task SizePreferences(int width, int height)
    {
        var reference = await Reference(PreferencesTarget());
        var window = host.Elements.InputForRef(reference);
        var area = Screen.FromHandle(window.Hwnd).WorkingArea;
        await invoke("windows_place_window", new
        {
            @ref = reference,
            placement = new
            {
                x = area.Left + (area.Width - width) / 2,
                y = area.Top + (area.Height - height) / 2,
                width,
                height
            }
        });
        using var process = Process.GetProcessById(window.ProcessId);
        if (!await Task.Run(() => process.WaitForInputIdle(15000)).WaitAsync(TimeSpan.FromSeconds(16)))
            throw new TimeoutException("Preferences did not finish resizing");
        await Task.Delay(500);
        window.EnsureAlive();
        var observed = Win32Desktop.GetWindowBounds(window.Hwnd);
        if (observed == null || observed.Value.Width != width || observed.Value.Height != height || !area.Contains(observed.Value))
            throw new InvalidOperationException($"Preferences size differs: expected {width}x{height}, observed {observed}");
    }
    public async Task AssertAutoFormattingState()
    {
        var pane = new ElementSelector(AutomationId: "DAX Editor.Auto Formatting");
        await AssertCheckboxes(pane, visibleOnly: false,
            ("Auto format code as you type", true), ("Auto-format function calls", true), ("Auto-indent", true),
            ("Auto-brace", true), ("Wrap selection", true), ("Space after functions", false),
            ("Newline after functions", false), ("Newline before operator", true), ("Pad parentheses", true),
            ("Fix measure/column qualifiers", true), ("Fix keyword/function casing", true), ("Fix object reference casing", true),
            ("Always quote tables", false), ("Always prefix extension columns", false));
        await Expect(new(new(Name: "Long format line limit", ControlType: "Spinner", Pattern: "Value"), pane, true), "120");
        await Expect(new(new(Name: "Short format line limit", ControlType: "Spinner", Pattern: "Value"), pane, true), "60");
        await Expect(new(new(Name: "Preferred keyword casing", ControlType: "ComboBox"), pane, true), "Default");
        await Expect(new(new(Name: "Preferred function casing", ControlType: "ComboBox"), pane, true), "Default");
        await Expect(new(new(Name: "Use default formatting settings", ControlType: "Button", Visible: true), pane, true));
    }
    public async Task AssertFileFormatsState()
    {
        var pane = new ElementSelector(AutomationId: "File Formats.General");
        await Expect(new(pane, IncludeOwned: true));
        await AssertCheckboxes(pane, visibleOnly: true,
            ("Ignore inferred objects", true), ("Ignore inferred properties", true), ("Ignore timestamps", true),
            ("Ignore lineage tags", false), ("Ignore privacy settings", false), ("Split multiline strings", true),
            ("Sort arrays by name", false), ("Include sensitive", false), ("Ignore incremental refresh partitions", false),
            ("Use PBIX filename as database name when serializing", true), ("Use latest default", true),
            ("Use workspace database", true), ("Create user options (.tmuo) file", true));
        await Expect(new(new(Name: "Default save format", ControlType: "ComboBox", Visible: true), pane, true), "Always ask");
        // TE3 3.26 uses 1600 here; the historical docs show 1500. Do not change the user's default.
        var compatibility = new Target(new(Name: "Compatibility level", ControlType: "ComboBox", Visible: true), pane, true);
        await Expect(compatibility, "1600 (Azure Analysis Services / SQL Server 2022+)");
        var element = await Resolve(compatibility);
        if (await Task.Run(() => element.Properties.IsEnabled.Value).WaitAsync(TimeSpan.FromSeconds(5)))
            throw new InvalidOperationException("Default compatibility level must be disabled");
    }
    // Compares each checkbox with its expected original state; preferences are never changed to match.
    private async Task AssertCheckboxes(ElementSelector pane, bool visibleOnly, params (string Name, bool On)[] expected)
    {
        foreach (var (name, on) in expected)
        {
            var control = await Resolve(new(new(Name: name, ControlType: "CheckBox", Visible: visibleOnly ? true : null), pane, true));
            var state = await Task.Run(() => control.Patterns.Toggle.Pattern.ToggleState.Value).WaitAsync(TimeSpan.FromSeconds(5));
            if (state != (on ? FlaUI.Core.Definitions.ToggleState.On : FlaUI.Core.Definitions.ToggleState.Off))
                throw new InvalidOperationException($"Checkbox differs from its expected state ({(on ? "on" : "off")}): {name}");
        }
    }
    private static Target SerializationModeTarget() => new(new(Name: "Serialization mode", ControlType: "ComboBox"),
        new(AutomationId: "File Formats.Save-to-folder"), true);
    public Task OpenSerializationModes() => Keys("Alt+Down", SerializationModeTarget());
    public Task CloseSerializationModes() => Keys("Escape", SerializationModeTarget());
    public async Task CaptureLanguageChoices(string path)
    {
        var language = new Target(new(Name: "User interface language", ControlType: "ComboBox", Visible: true),
            new(AutomationId: "Tabular Editor.User Interface"), true);
        await Expect(language, "English");
        Exception? failure = null;
        nint popupHwnd = 0;
        try
        {
            await Keys("Alt+Down", language);
            await Task.Delay(500);
            var main = host.Sessions.GetInputTarget(handle);
            var preferences = host.Elements.InputForRef(await Reference(PreferencesTarget()));
            var popups = await ObservePopups(main.ProcessId, maxWindows: 5, main.Hwnd, preferences.Hwnd);
            File.WriteAllText(Path.ChangeExtension(path, ".uia.json"), JsonSerializer.Serialize(popups, Indented));
            if (popups.Any(p => !p.Tree.Complete))
                throw new InvalidOperationException("Language popup observation incomplete");
            var matches = popups.Where(p => p.Tree.Elements.Any(e => e.ControlType == "ListItem")).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("Expected one language list popup");
            var popup = matches[0];
            popupHwnd = new nint(popup.Hwnd);
            var lists = popup.Tree.Elements.Where(e => e.ControlType == "List").ToArray();
            if (lists.Length != 1 || popup.Tree.Elements.Where(e => e.ControlType == "ListItem").Any(e => e.Depth <= lists[0].Depth))
                throw new InvalidOperationException("Expected one language list with descendant items");
            VerifyLanguageChoices(popup.Tree.Elements.Where(e => e.ControlType == "ListItem").ToArray());
            var bounds = popup.Bounds ?? throw new InvalidOperationException("Language popup bounds unavailable");
            var combo = await Resolve(language);
            var comboBounds = await Task.Run(() => combo.BoundingRectangle).WaitAsync(TimeSpan.FromSeconds(5));
            if (Math.Abs(bounds.Left - comboBounds.Left) > 3 || Math.Abs(bounds.Width - comboBounds.Width) > 6 ||
                Math.Min(Math.Abs(bounds.Top - comboBounds.Bottom), Math.Abs(bounds.Bottom - comboBounds.Top)) > 3)
                throw new InvalidOperationException("Language popup is not adjacent to its combo box");
            var preferencesBounds = Win32Desktop.GetWindowBounds(preferences.Hwnd)
                ?? throw new InvalidOperationException("Preferences disappeared");
            var scene = System.Drawing.Rectangle.Union(preferencesBounds, bounds);
            CaptureScene(path, preferences, InputTarget.Capture(popupHwnd, main.ProcessId),
                scene, modal: false, includeOutsideOwner: true, popupShadowPadding: 10);
        }
        catch (Exception error) { failure = error; throw; }
        finally
        {
            try
            {
                await Keys("Escape", language);
                if (popupHwnd != 0)
                    await WaitForAbsence(() => Win32Desktop.GetTopLevelWindows().Any(w => w.Hwnd == popupHwnd));
                await Expect(language, "English");
            }
            catch (Exception cleanup)
            {
                if (failure != null) throw new AggregateException("Language capture and popup cleanup failed", failure, cleanup);
                throw;
            }
        }
    }
    public static void VerifyLanguageChoices(IReadOnlyList<ElementInfo> items)
    {
        // TE3 3.26.3 capitalizes Spanish/French; historical docs used lowercase.
        string[] expected = ["English", "Deutsch (Beta)", "Español (Preview)", "Français (Beta)",
            "中文(简体) (Preview)", "日本語 (Beta)"];
        if (!items.Select(e => e.Name).SequenceEqual(expected) || items.Any(e => e.Offscreen) ||
            items.Count(e => e.Selected == true) != 1 || items[0].Selected != true)
            throw new InvalidOperationException("Language choices differ from verified TE3 3.26.3; inspect popup map");
    }
    public async Task StageCustomSerialization()
    {
        await Expect(SerializationModeTarget(), "Database.json (default)");
        await Keys("Down", SerializationModeTarget());
        await Expect(SerializationModeTarget(), "Database.json (customizable)");
    }
    public async Task StageOriginalSerializationOptions()
    {
        var pane = new ElementSelector(AutomationId: "File Formats.Save-to-folder");
        foreach (var name in new[] { "Serialize relationships on from-tables", "Serialize perspective membership info on objects",
            "Serialize translations on translated objects" })
        {
            var target = new Target(new(Name: name, ControlType: "CheckBox", Visible: true), pane, true);
            var element = await Resolve(target);
            var state = await Task.Run(() => element.Patterns.Toggle.Pattern.ToggleState.Value).WaitAsync(TimeSpan.FromSeconds(5));
            if (state != FlaUI.Core.Definitions.ToggleState.Off) throw new InvalidOperationException("Unexpected initial checkbox: " + name);
            await Click(target);
            element = await Resolve(target);
            state = await Task.Run(() => element.Patterns.Toggle.Pattern.ToggleState.Value).WaitAsync(TimeSpan.FromSeconds(5));
            if (state != FlaUI.Core.Definitions.ToggleState.On) throw new InvalidOperationException("Checkbox did not turn on: " + name);
        }
        var tree = new ElementSelector(Name: "Serialization depth", ControlType: "Tree");
        await Click(new(new(Value: "Tables", ControlType: "TreeItem"), tree, true));
        await Keys("Right", new(tree, pane, true));
        await Expect(new(new(Value: "Columns", ControlType: "TreeItem"), tree, true));
        await Click(new(new(Value: "Data Sources", ControlType: "TreeItem"), tree, true));
        await AssertSerializationTreeState();
    }
    public async Task AssertSerializationTreeState()
    {
        var pane = new ElementSelector(AutomationId: "File Formats.Save-to-folder");
        var tree = new ElementSelector(Name: "Serialization depth", ControlType: "Tree");
        var selected = await Resolve(new(new(Value: "Data Sources", ControlType: "TreeItem"), tree, true));
        if (!await Task.Run(() => selected.Patterns.SelectionItem.Pattern.IsSelected.Value).WaitAsync(TimeSpan.FromSeconds(5)))
            throw new InvalidOperationException("Data Sources must be selected");
        var prefix = await Resolve(new(new(Name: "Prefix file names sequentially", ControlType: "CheckBox"), pane, true));
        if (await Task.Run(() => prefix.Patterns.Toggle.Pattern.ToggleState.Value).WaitAsync(TimeSpan.FromSeconds(5)) != FlaUI.Core.Definitions.ToggleState.Off)
            throw new InvalidOperationException("Sequential filename prefix must be off");
        foreach (var value in new[] { "Data Sources", "Shared Expressions", "Roles", "Tables", "Columns", "Hierarchies" })
        {
            var row = await Resolve(new(new(Value: value, ControlType: "TreeItem", Visible: true), tree, true));
            // DevExpress exposes checked tree rows through MSAA STATE_SYSTEM_CHECKED, not UIA Toggle.
            var checkedState = await Task.Run(() => row.Patterns.LegacyIAccessible.IsSupported
                ? (uint)row.Patterns.LegacyIAccessible.Pattern.State.Value : 0).WaitAsync(TimeSpan.FromSeconds(5));
            if ((checkedState & 0x10) == 0) throw new InvalidOperationException("Serialization row not checked via accessibility: " + value);
        }
    }
    public async Task SaveSerializationModes(string path)
    {
        var owner = host.Sessions.GetInputTarget(handle);
        var preferences = host.Elements.InputForRef(await Reference(PreferencesTarget()));
        string[] expected = ["Database.json (default)", "Database.json (customizable)", "TMDL"];
        // Alt+Down can return before the dropdown window exists, so poll until one popup holds the choices.
        var clock = Stopwatch.StartNew();
        while (true)
        {
            var popups = await ObservePopups(owner.ProcessId, maxWindows: 6, owner.Hwnd, preferences.Hwnd);
            var lists = popups.Where(p => p.Tree.Elements.Any(e => e.ControlType == "ListItem")).ToArray();
            var problem = "Serialization mode popup not found";
            if (lists.Length == 1 && lists[0].Tree.Complete)
            {
                var items = lists[0].Tree.Elements.Where(e => e.ControlType == "ListItem").ToArray();
                if (items.Select(e => e.Name).SequenceEqual(expected) && items.Count(e => e.Selected == true) == 1 && items[0].Selected == true)
                {
                    File.WriteAllText(path, JsonSerializer.Serialize(popups, Indented));
                    return;
                }
                problem = "Serialization mode popup differs from observed choices";
            }
            if (clock.Elapsed > TimeSpan.FromSeconds(5)) throw new InvalidOperationException(problem);
            await Task.Delay(100);
        }
    }
    public async Task AssertFileFormatsAnnotationRows()
    {
        var result = await Task.Run(() => query.Find(handle, new(ControlType: "TreeItem", Visible: true),
            new(AutomationId: "treePreferences"), maxResults: 100, includeOwned: true,
            budget: new SearchBudget(1000, TimeSpan.FromSeconds(3)))).WaitAsync(TimeSpan.FromSeconds(5));
        var rows = result.Elements;
        var parent = rows.FindIndex(row => row.Value == "File Formats");
        if (result.Truncated || result.Unreadable != 0 || parent < 0 || parent + 2 >= rows.Count ||
            rows.Count(row => row.Value == "File Formats") != 1 || rows[parent].Selected != true ||
            rows[parent + 1].Value != "General" || rows[parent + 2].Value != "Save-to-folder")
            throw new InvalidOperationException("File Formats annotation rows are not visible in the expected order");
        var reference = await Reference(PreferencesTarget());
        var dialog = host.Elements.InputForRef(reference);
        await Task.Run(() =>
        {
            // UIA reports the dialog's client bounds; PrintWindow includes its title bar and border.
            dialog.EnsureAlive();
            var origin = (Win32Desktop.GetWindowBounds(dialog.Hwnd)
                ?? throw new InvalidOperationException("Preferences window disappeared")).Location;
            foreach (var (index, x, y) in new[] { (parent + 1, 125, 718), (parent + 2, 165, 742) })
            {
                var element = host.Elements.GetElement(rows[index].Ref) ?? throw new InvalidOperationException("Stale annotation row");
                var bounds = element.BoundingRectangle;
                bounds.Offset(-origin.X, -origin.Y);
                if (!bounds.Contains(x, y) || Math.Abs(bounds.Top + bounds.Height / 2 - y) > 1)
                    throw new InvalidOperationException($"File Formats annotation {rows[index].Value}: observed {bounds}, expected tip ({x},{y}); review geometry");
            }
        }).WaitAsync(TimeSpan.FromSeconds(5));
    }
    public async Task AssertDaxGeneralState()
    {
        var pane = new ElementSelector(AutomationId: "DAX Editor.General");
        await Expect(new(pane, IncludeOwned: true));
        await AssertCheckboxes(pane, visibleOnly: true,
            ("Line numbers", true), ("Code folding", true), ("Visible whitespace", false),
            ("Indentation guides", true), ("Use tabs", false),
            ("Use daxformatter.com instead of built-in formatter", false));
        foreach (var (name, value) in new[]
        {
            ("Comment style:", "Slashes"), ("Locale", "US (A, B, C, 1234.00)"),
            ("Semantic engine", "Auto-detect"), ("Scalar predicates", "Auto-detect"),
            ("Directional CF", "Auto-detect"), ("Date Literals", "Auto-detect"),
            ("Measure/Agg Shortcut", "Auto-detect"), ("Table-named variables", "Auto-detect"),
            ("Default DAX formatting", "Long lines"), ("Documentation URL", "https://dax.guide/{0}")
        })
            await Expect(new(new(Name: name, ControlType: "ComboBox", Pattern: "Value", Visible: true), pane, true), value);
        await Expect(new(new(Name: "Tab/indent width", ControlType: "Spinner", Pattern: "Value", Visible: true), pane, true), "4");
        await Expect(new(new(Name: "Request Timeout (ms)", ControlType: "Spinner", Pattern: "Value", Visible: true), pane, true), "5000");
    }
    public async Task AssertCodeActionsState()
    {
        var rows = await Task.Run(() => query.Find(handle, new(ControlType: "TreeItem", Visible: true),
            new(AutomationId: "treePreferences"), maxResults: 100, includeOwned: true,
            budget: new SearchBudget(1000, TimeSpan.FromSeconds(3)))).WaitAsync(TimeSpan.FromSeconds(5));
        if (rows.Truncated || rows.Unreadable != 0 || rows.Elements.FirstOrDefault()?.Value != "Pivot Grid" ||
            !rows.Elements.Any(row => row.Value == "Model Deployment"))
            throw new InvalidOperationException("Code Actions tree framing must start at Pivot Grid and include Model Deployment");
        var pane = new ElementSelector(AutomationId: "DAX Editor.Code Actions");
        await AssertCheckboxes(pane, visibleOnly: false, ("Show code actions", true), ("Apply variable casing", false));
        await Expect(new(new(Name: "Variable prefix", ControlType: "ComboBox"), pane, true), "_");
        await Expect(new(new(Name: "Extension column prefix", ControlType: "ComboBox"), pane, true), "@");
        var casing = new Target(new(Name: "Preferred variable casing", ControlType: "ComboBox"), pane, true);
        await Expect(casing, "Default");
        var element = await Resolve(casing);
        if (await Task.Run(() => element.Properties.IsEnabled.Value).WaitAsync(TimeSpan.FromSeconds(5)))
            throw new InvalidOperationException("Preferred variable casing must be disabled");
    }
    public async Task SelectPreferencesSection(string section)
    {
        var row = new Target(new(ControlType: "TreeItem", Value: section), new(AutomationId: "treePreferences"), true);
        await Click(row);
        var selected = await Resolve(row);
        if (!await Task.Run(() => selected.Patterns.SelectionItem.Pattern.IsSelected.Value).WaitAsync(TimeSpan.FromSeconds(5)))
            throw new InvalidOperationException("Preferences section is not selected: " + section);
        await SearchPreferences("");
        var tree = new Target(new(AutomationId: "treePreferences"), PreferencesTarget().Selector, true);
        var visible = false;
        for (var page = 0; page < 8; page++)
        {
            visible = await Task.Run(() => query.IsPresent(PreferencesHandle(), row.Selector, row.Within, false,
                new SearchBudget(1000, TimeSpan.FromSeconds(2)))).WaitAsync(TimeSpan.FromSeconds(5));
            if (visible) break;
            await Keys("PageDown", tree);
        }
        if (!visible) throw new InvalidOperationException("Preferences section did not become visible after clearing search");
        await Click(row);
        selected = await Resolve(row);
        if (!await Task.Run(() => selected.Patterns.SelectionItem.Pattern.IsSelected.Value).WaitAsync(TimeSpan.FromSeconds(5)))
            throw new InvalidOperationException("Clearing search changed the selected Preferences section");
    }
    public async Task SelectPreferencesFirstChild(string parent, string child)
    {
        await SearchPreferences(parent);
        await SelectPreferencesSection(parent);
        var tree = new Target(new(AutomationId: "treePreferences"), PreferencesTarget().Selector, true);
        async Task<List<ElementInfo>> Rows()
        {
            var result = await Task.Run(() => query.Find(handle, new(ControlType: "TreeItem", Visible: true),
                tree.Selector, maxResults: 100, includeOwned: true, budget: new SearchBudget(1000, TimeSpan.FromSeconds(3))))
                .WaitAsync(TimeSpan.FromSeconds(5));
            if (result.Truncated || result.Unreadable != 0) throw new InvalidOperationException("Preferences tree observation incomplete");
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
        if (parents.Length != 1 || selected.Length != 1 || selected[0].row.Value != child ||
            selected[0].index != parents[0].index + 1)
            throw new InvalidOperationException($"Expected selected first child {parent} > {child}");
    }
    public async Task SaveControlMap(string path, ElementSelector within)
    {
        var scoped = await Task.Run(() => query.Find(handle, new(Visible: true), within, maxResults: 200,
            includeOwned: true, budget: new SearchBudget(1000, TimeSpan.FromSeconds(3)))).WaitAsync(TimeSpan.FromSeconds(5));
        File.WriteAllText(path, JsonSerializer.Serialize(scoped, Indented));
    }

    public async Task AssertAutoRollbackEnabled()
    {
        var element = await Resolve(AutoRollbackTarget());
        reportStep?.Invoke("Assert Auto-rollback toggle is on (read only)");
        var enabled = await Task.Run(() => element.Patterns.Toggle.IsSupported &&
            element.Patterns.Toggle.Pattern.ToggleState.Value == FlaUI.Core.Definitions.ToggleState.On)
            .WaitAsync(TimeSpan.FromSeconds(5));
        if (!enabled) throw new InvalidOperationException("Auto-rollback must be on for this original-image capture");
    }

    private sealed record PopupObservation(long Hwnd, string Title, System.Drawing.Rectangle? Bounds, QueryResult Tree);

    // Popups are separate top-level windows; searching them directly avoids spending the budget on the editor.
    private async Task<PopupObservation[]> ObservePopups(int processId, int? maxWindows, params nint[] exclude)
    {
        var windows = Win32Desktop.GetTopLevelWindows(processId).Where(w => !w.IsCloaked && !exclude.Contains(w.Hwnd)).ToArray();
        if (windows.Length > maxWindows) throw new InvalidOperationException($"Popup discovery incomplete: {windows.Length} candidate windows");
        return await Task.Run(() => windows.Select(w => new PopupObservation(w.Hwnd.ToInt64(), w.Title, Win32Desktop.GetWindowBounds(w.Hwnd),
                query.Find(host.Sessions.RegisterNativeWindow(w.Hwnd, processId), new(Visible: true),
                    maxDepth: 5, maxResults: 100, budget: new SearchBudget(1000, TimeSpan.FromSeconds(3))))).ToArray())
            .WaitAsync(TimeSpan.FromSeconds(5));
    }

    // UIA can report keyboard focus shortly after the key press, so poll briefly before failing.
    private async Task RequireKeyboardFocus(Target target, string failure)
    {
        var clock = Stopwatch.StartNew();
        do
        {
            var element = await Resolve(target);
            if (await Task.Run(() => element.Properties.HasKeyboardFocus.Value).WaitAsync(TimeSpan.FromSeconds(5))) return;
            await Task.Delay(100);
        } while (clock.Elapsed < TimeSpan.FromSeconds(2));
        throw new InvalidOperationException(failure);
    }

    private async Task<AutomationElement> Resolve(Target target, string? expected = null)
    {
        reportStep?.Invoke($"Find {target.Selector}; expected value: {expected ?? "(any)"}");
        Exception? last = null;
        var clock = Stopwatch.StartNew();
        do
        {
            OperationContext.Check();
            try
            {
                return await Task.Run(() =>
                {
                    // Avoid reattaching the disabled owner and every popup for a
                    // known dialog. Keep the lookup and uniqueness check local.
                    var preferences = IsPreferencesLookup(target);
                    var queryHandle = preferences ? PreferencesHandle() : handle;
                    var within = preferences && target.Within == PreferencesTarget().Selector ? null : target.Within;
                    var element = query.Resolve(queryHandle, target.Selector, within, !preferences && target.IncludeOwned,
                        budget: new SearchBudget(3000, TimeSpan.FromSeconds(2)));
                    var actual = element.Patterns.Value.IsSupported ? element.Patterns.Value.Pattern.Value.ValueOrDefault : element.Properties.Name.ValueOrDefault;
                    if (expected != null && actual != expected) throw new InvalidOperationException($"Expected '{expected}', observed '{actual}'");
                    return element;
                }).WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (System.Reflection.AmbiguousMatchException) { throw; }
            catch (OperationCanceledException) { throw; }
            catch (TimeoutException) { throw; } // A hung provider must not spawn retry workers.
            catch (Exception ex) { last = ex; }
            await Task.Delay(100);
        } while (clock.Elapsed < TimeSpan.FromSeconds(15));
        throw new TimeoutException($"TE3 target {target.Selector}: {last?.Message}", last);
    }
    private async Task<string> Reference(Target target) => host.Elements.Register(handle, await Resolve(target));
    public async Task Expect(Target target, string? text = null) => _ = await Resolve(target, text);
    private async Task Click(Target target, bool physical = true)
    {
        var reference = await Reference(target);
        reportStep?.Invoke($"Click {target.Selector}");
        await invoke("windows_click", new { @ref = reference, physical });
    }
    private async Task Fill(Target target, string value)
    {
        var reference = await Reference(target);
        reportStep?.Invoke($"Fill {target.Selector}");
        await invoke("windows_fill", new { @ref = reference, value });
    }
    private async Task Keys(string chord, Target? target = null)
    {
        var reference = target == null ? null : await Reference(target);
        reportStep?.Invoke($"Keys {chord}; target: {target?.Selector.ToString() ?? handle}");
        await invoke("windows_send_keys", new { handle, @ref = reference, chord });
    }
    private async Task Absent(Target target)
    {
        reportStep?.Invoke($"Wait for absence: {target.Selector}");
        await WaitForAbsenceAsync(() => Task.Run(() => query.IsPresent(handle, target.Selector, target.Within, target.IncludeOwned,
            new SearchBudget(3000, TimeSpan.FromSeconds(2)))).WaitAsync(TimeSpan.FromSeconds(5)));
    }
    public static async Task WaitForAbsence(Func<bool> present, int timeoutMs = 15000, int settleMs = 500)
        => await WaitForAbsenceAsync(() => Task.FromResult(present()), timeoutMs, settleMs);
    private static async Task WaitForAbsenceAsync(Func<Task<bool>> present, int timeoutMs = 15000, int settleMs = 500)
    {
        var total = Stopwatch.StartNew();
        Stopwatch? absent = null;
        Exception? lastUnavailable = null;
        while (total.ElapsedMilliseconds < timeoutMs)
        {
            try
            {
                if (await present()) absent = null;
                else
                {
                    absent ??= Stopwatch.StartNew();
                    if (absent.ElapsedMilliseconds >= settleMs) return;
                }
            }
            catch (Exception error) when (error is FlaUI.Core.Exceptions.ElementNotAvailableException ||
                error is System.Runtime.InteropServices.COMException { HResult: unchecked((int)0x80040201) })
            {
                // A window can disappear between enumeration and a property read.
                // Re-observe from fresh roots; a failed read never proves absence.
                absent = null;
                lastUnavailable = error;
            }
            await Task.Delay(100);
        }
        throw new TimeoutException("Target did not remain absent for the settle period", lastUnavailable);
    }
    private async Task Count(Target target, int expected)
    {
        reportStep?.Invoke($"Wait for {expected} rows: {target.Selector}");
        var clock = Stopwatch.StartNew();
        do
        {
            var result = await Task.Run(() => query.Find(handle, target.Selector, target.Within, maxResults: expected + 1, includeOwned: target.IncludeOwned,
                budget: new SearchBudget(3000, TimeSpan.FromSeconds(2)))).WaitAsync(TimeSpan.FromSeconds(5));
            if (!result.Truncated && result.Unreadable == 0 && result.Elements.Count == expected) return;
            await Task.Delay(100);
        } while (clock.Elapsed < TimeSpan.FromSeconds(15));
        throw new TimeoutException($"Expected exactly {expected} rows");
    }
    public async Task Capture(string path, Target? target = null, CaptureFrame? frame = null)
    {
        var reference = target == null ? null : await Reference(target);
        reportStep?.Invoke($"Capture {Path.GetFileName(path)}");
        await invoke("windows_screenshot", new
        {
            handle,
            @ref = reference,
            savePath = path,
            includeImage = false,
            background = target == null || target.Selector.RootOnly,
            frame
        });
    }
    public async Task CaptureScalarScene(string path)
    {
        var owner = host.Sessions.GetInputTarget(handle);
        owner.EnsureAlive();
        var ownerBounds = Win32Desktop.GetWindowBounds(owner.Hwnd)
            ?? throw new InvalidOperationException("Owner bounds unavailable");
        var reference = await Reference(ScriptOutputTarget());
        var dialog = host.Elements.InputForRef(reference);
        var requested = new System.Drawing.Rectangle(ownerBounds.Left + 96, ownerBounds.Top + 192, 555, 254);
        reportStep?.Invoke("Place scalar output dialog beside its source");
        await invoke("windows_place_window", new
        {
            @ref = reference,
            placement = new
            {
                x = requested.X,
                y = requested.Y,
                width = requested.Width,
                height = requested.Height
            }
        });
        // Native bounds change before DevExpress finishes repainting its resized controls.
        await Task.Delay(500);
        var observed = Win32Desktop.GetWindowBounds(dialog.Hwnd)
            ?? throw new InvalidOperationException("Output dialog bounds unavailable");
        // Keep OS minimum sizes; never squeeze or stretch pixels to match an old version.
        var scene = ScalarSceneBounds(ownerBounds, observed, Screen.FromHandle(owner.Hwnd).WorkingArea);
        CaptureScene(path, owner, dialog, scene, modal: true);
    }
    private void CaptureScene(string path, InputTarget owner, InputTarget dialog, System.Drawing.Rectangle scene, bool modal,
        bool includeOutsideOwner = false, int? popupShadowPadding = null)
    {
        var ownerBounds = Win32Desktop.GetWindowBounds(owner.Hwnd) ?? throw new InvalidOperationException("Owner disappeared");
        var observed = Win32Desktop.GetWindowBounds(dialog.Hwnd) ?? throw new InvalidOperationException("Popup disappeared");
        void VerifyScene()
        {
            owner.EnsureAlive(); dialog.EnsureAlive();
            if (Win32Desktop.GetForegroundWindow() != (modal ? dialog.Hwnd : owner.Hwnd) ||
                Win32Desktop.GetWindowBounds(owner.Hwnd) != ownerBounds ||
                Win32Desktop.GetWindowBounds(dialog.Hwnd) != observed)
                throw new InvalidOperationException("Scene focus or geometry changed");
            if ((!includeOutsideOwner && !ownerBounds.Contains(scene)) ||
                (includeOutsideOwner && scene != System.Drawing.Rectangle.Union(ownerBounds, observed)) ||
                !Screen.AllScreens.Any(s => s.WorkingArea.Contains(scene)))
                throw new InvalidOperationException($"Scene {scene} is clipped; owner {ownerBounds}, dialog {observed}, work areas {string.Join("; ", Screen.AllScreens.Select(s => s.WorkingArea))}");
            var windows = Win32Desktop.GetTopLevelWindows();
            var aboveOwner = windows.TakeWhile(w => w.Hwnd != owner.Hwnd).ToArray();
            var obstructions = aboveOwner.Where(w =>
                !w.IsCloaked && w.Hwnd != dialog.Hwnd &&
                !IsPopupShadow(w, Win32Desktop.GetWindowBounds(w.Hwnd), dialog.ProcessId, observed, modal, popupShadowPadding) &&
                (Win32Desktop.GetWindowBounds(w.Hwnd)?.IntersectsWith(scene) ?? true)).ToArray();
            if (!windows.Any(w => w.Hwnd == owner.Hwnd) || obstructions.Length > 0)
                throw new InvalidOperationException($"Scene popup {observed} obscured: " + string.Join("; ", obstructions.Select(w =>
                    $"HWND={w.Hwnd}, PID={w.ProcessId}, title={w.Title}, bounds={Win32Desktop.GetWindowBounds(w.Hwnd)}")));
        }
        reportStep?.Invoke("Capture unobscured owner/popup scene (actual screen pixels)");
        VerifyScene();
        using var capture = FlaUI.Core.Capturing.Capture.Rectangle(scene);
        VerifyScene();
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        capture.Bitmap.Save(output, System.Drawing.Imaging.ImageFormat.Png);
    }
    public async Task CaptureCalculationGroupMenu(string path)
    {
        var modelMenu = new Target(new(Name: "Model", ControlType: "MenuItem"), new(Name: "Main menu", ControlType: "MenuBar"));
        var menuBarItem = await Resolve(modelMenu);
        var ownerIdentity = host.Sessions.GetInputTarget(handle);
        var menuBarPoint = await Task.Run(menuBarItem.GetClickablePoint).WaitAsync(TimeSpan.FromSeconds(5));
        using (var input = new GuardedInput(ownerIdentity))
        {
            if (Win32Desktop.WindowAt(menuBarPoint) != ownerIdentity.Hwnd) throw new InvalidOperationException("Model menu is obscured");
            reportStep?.Invoke("Open Model menu");
            input.Send(() => FlaUI.Core.Input.Mouse.MoveTo(menuBarPoint));
        }
        // Invoke can remain pending for TE3 menu popups; use the guarded input path.
        await Click(modelMenu);
        await Task.Delay(500);
        var discovery = await ObservePopups(ownerIdentity.ProcessId, maxWindows: null, ownerIdentity.Hwnd);
        File.WriteAllText(Path.ChangeExtension(path, ".uia.json"), JsonSerializer.Serialize(discovery, Indented));
        var target = new Target(new(Name: "Add Calculation Group", ControlType: "Button"), null, true);
        var element = await Resolve(target);
        var popup = await MenuPopup(element);
        var owner = host.Sessions.GetInputTarget(handle);
        // Keep the pointer on the menu bar, outside the popup, so tooltips cannot hide rows.
        // This is the observed enabled-item order; assert every step instead of blind key counts.
        await Keys("Home");
        foreach (var name in new[] { "Deploy...", "Serialization options...", "Import tables...", "Script DAX",
            "Refresh model", "Add Table", "Add Calculated Table", "Add Calculation Group" })
        {
            if (name != "Deploy...") await Keys("Down");
            await RequireKeyboardFocus(new(new(Name: name), null, true), "Expected focused menu item: " + name);
        }
        var ownerBounds = Win32Desktop.GetWindowBounds(owner.Hwnd) ?? throw new InvalidOperationException("Owner disappeared");
        var bounds = Win32Desktop.GetWindowBounds(popup) ?? throw new InvalidOperationException("Menu disappeared");
        await Task.Delay(500);
        await RequireKeyboardFocus(target, "Calculation group menu item is not highlighted/focused");
        var workArea = Screen.FromHandle(owner.Hwnd).WorkingArea;
        var x = Math.Max(ownerBounds.Left, workArea.Left);
        var y = Math.Max(ownerBounds.Top, workArea.Top);
        var scene = new System.Drawing.Rectangle(x, y, Math.Max(1018, bounds.Right - x + 16), Math.Max(665, bounds.Bottom - y + 16));
        CaptureScene(path, owner, InputTarget.Capture(popup, owner.ProcessId), scene, modal: false);
        await Keys("Escape");
        await Absent(target);
    }
    public static System.Drawing.Rectangle ScalarSceneBounds(System.Drawing.Rectangle owner,
        System.Drawing.Rectangle dialog, System.Drawing.Rectangle workArea)
    {
        // Maximized windows have invisible borders beyond the monitor (-9 on this desktop).
        // Anchor the source strip inside the visible work area, not that invisible border.
        var x = Math.Max(owner.Left + 8, workArea.Left);
        var y = Math.Max(owner.Top + 100, workArea.Top);
        return new(x, y, Math.Max(704, dialog.Right - x + 15), Math.Max(361, dialog.Bottom - y + 15));
    }
    public static bool IsPopupShadow(Win32WindowInfo window, System.Drawing.Rectangle? bounds,
        int processId, System.Drawing.Rectangle dialog, bool modal = true, int? padding = null)
    {
        // Observed TE3 3.26.3 shadows: modal/language list 10 px, menu 7 px.
        var margin = padding ?? (modal ? 10 : 7);
        return window.ProcessId == processId && window.Title.Length == 0 &&
            bounds == System.Drawing.Rectangle.Inflate(dialog, margin, margin);
    }
    public async Task OpenCSharpScriptFile(string file, string script)
    {
        // Leave transient property editors before invoking an application command.
        await WaitForMainWindowReady();
        await Click(ExpressionTabTarget());
        await Keys("Ctrl+O");
        await Expect(OpenFileDialogTarget());
        await Fill(OpenFileNameTarget(), file);
        await Keys("Enter", OpenFileNameTarget());
        await Absent(OpenFileDialogTarget());
        await Expect(CsharpTextTarget(), script);
    }
    public async Task CloseCSharpDocument()
    {
        await Keys("Ctrl+W", CsharpInputTarget());
        await Absent(CsharpTextTarget());
    }
    public async Task CloseOptionalDocument(string title)
    {
        var target = DocumentTabTarget(title);
        reportStep?.Invoke($"Check optional document: {title}");
        var present = await Task.Run(() => query.IsPresent(handle, target.Selector,
            budget: new SearchBudget(3000, TimeSpan.FromSeconds(2)))).WaitAsync(TimeSpan.FromSeconds(5));
        if (!present) return;
        await Click(target);
        await Keys("Ctrl+W");
        await Absent(target);
    }
    public async Task AssertTwoOutputRows()
    {
        await Expect(ScriptOutputTarget());
        await Count(ScriptOutputRowsTarget(), 2);
    }
    public async Task AssertOutputCell(string row, string value)
    {
        await Expect(ScriptOutputCellTarget(row), value);
    }
    public async Task AssertNoOutputObjectName(string property)
    {
        await Absent(ScriptOutputPropertyTarget(property));
    }
    public async Task AssertOutputProperty(string property, string value)
    {
        await Expect(ScriptOutputPropertyTarget(property), value);
    }
    public async Task AssertScalarOutput(string value)
    {
        await Expect(ScriptOutputTarget());
        await Expect(ScriptOutputTextTarget(), value);
    }
    public async Task CloseScriptOutput()
    {
        await Click(ScriptOutputCloseTarget());
    }
    public async Task AssertOutputClosed()
    {
        // Closing a modal and returning control to its owner are separate transitions.
        // Require the owner to be enabled before querying the disappearing dialog.
        await WaitForMainWindowReady();
        await Absent(ScriptOutputTarget());
    }
    private async Task WaitForMainWindowReady()
    {
        reportStep?.Invoke("Wait for main window to be enabled after modal transition");
        var owner = host.Sessions.GetInputTarget(handle);
        await WaitForAbsence(() =>
        {
            owner.EnsureAlive();
            return !Win32Desktop.IsWindowEnabled(owner.Hwnd);
        });
    }
    public async Task UndoModelChange()
    {
        await Keys("CTRL+Z", TomExplorerTarget());
    }
    public async Task ShowPreviewDescription(string measure, string before, string after)
    {
        await Click(PreviewSalesRowTarget());
        await Keys("Right", PreviewTreeTarget());
        await Keys("Right", PreviewTreeTarget());
        await Expect(PreviewMeasuresRowTarget());
        await Click(PreviewMeasuresRowTarget());
        await Keys("Right", PreviewTreeTarget());
        await Keys("Right", PreviewTreeTarget());
        await Expect(PreviewMeasureRowTarget(measure));
        await Click(PreviewMeasureRowTarget(measure));
        await Keys("Right", PreviewTreeTarget());
        await Keys("Right", PreviewTreeTarget());
        await Expect(PreviewDescriptionRowTarget(before, after), null);
    }
    public async Task SelectDocument(string tab)
    {
        await Click(DocumentTabTarget(tab));
    }
    public async Task RefreshBpa()
    {
        await Click(BpaRefreshTarget());
    }
    public async Task ShowBpaBottom()
    {
        await Keys("Ctrl+End", BpaTreeTarget());
    }
    public async Task AssertBpaViolation(string rule)
    {
        await Expect(BpaRuleTarget(rule));
    }
    public async Task AssertBpaClear(string rule)
    {
        await Absent(BpaRuleTarget(rule));
    }
    public async Task CollapseObjectTree()
    {
        await Click(CollapseTomTarget(), false);
    }
    public async Task OpenTom()
    {
        await Click(TomTabTarget());
    }
    public async Task SetProperty(string property, string value)
    {
        await Click(PropertyTarget(property));
        await Fill(PropertyInputTarget(), value);
        await Keys("Enter", PropertyInputTarget());
        await Expect(PropertyTarget(property), value);
    }
    public async Task PreviewCSharpScript()
    {
        await Click(RunPreviewTarget());
        await Expect(ScriptPreviewTarget());
    }
    public async Task AcceptScriptPreview()
    {
        await Click(PreviewAcceptTarget());
        await Absent(ScriptPreviewTarget());
    }
    public async Task CancelScriptPreview()
    {
        await Click(PreviewRevertTarget());
        await Absent(ScriptPreviewTarget());
    }
    public async Task RunCSharpScript()
    {
        await Click(RunScriptTarget());
    }
    public async Task OpenBpa()
    {
        await Click(BpaTabTarget());
    }
    public async Task AssertProperty(string property, string value)
    {
        await Expect(PropertyTarget(property), value);
    }
    public async Task ResetSearch()
    {
        await Fill(SearchTarget(), "");
    }
    public async Task SelectObject(string objectName)
    {
        await Click(ObjectTarget(objectName));
    }
    public async Task ExpandSelected()
    {
        await Keys("Right", TomExplorerTarget());
    }
    public async Task SetExpression(string expression)
    {
        await Click(ExpressionTabTarget());
        await Fill(ExpressionInputTarget(), expression);
        await Expect(ExpressionTarget(), expression);
        await Keys("F5");
    }
    public async Task AssertMissingMeasureError()
    {
        await Expect(MissingMeasureDiagnosticTarget());
    }
    public async Task AssertNoErrors()
    {
        await Absent(ErrorsTarget());
    }
    public async Task Save()
    {
        await Keys("Ctrl+S");
    }
    public async Task CreateMeasure()
    {
        await Keys("Alt+1", TomExplorerTarget());
    }
    public async Task RenameNewMeasure(string name)
    {
        await Fill(NewMeasureNameTarget(), name);
        await Keys("Enter");
    }
    public async Task NewQuery()
    {
        await Click(NewQueryTarget());
    }
    public async Task RunQuery(string query)
    {
        await Fill(QueryInputTarget(), query);
        await Expect(QueryTextTarget(), query);
        await Keys("F5", QueryInputTarget());
    }
    public async Task AssertQueryResult(string result)
    {
        await Expect(QueryResultTarget(), result);
    }
    public async Task DefaultLayout()
    {
        await Click(WindowMenuTarget());
        var element = await Resolve(DefaultLayoutTarget());
        var owner = host.Sessions.GetInputTarget(handle);
        var popup = await MenuPopup(element);
        // TE3's non-activating menu is a same-process window without a native owner.
        // Keep the main window foreground and require hit testing against this exact popup.
        var point = await Task.Run(element.GetClickablePoint).WaitAsync(TimeSpan.FromSeconds(5));
        using var input = new GuardedInput(owner with { HitHwnd = popup });
        if (Win32Desktop.GetProcessId(popup) != owner.ProcessId || Win32Desktop.WindowAt(point) != popup)
            throw new InvalidOperationException("Default layout click point is obscured");
        reportStep?.Invoke("Apply default layout");
        input.Send(() => FlaUI.Core.Input.Mouse.Click(point));
    }
    private async Task<nint> MenuPopup(AutomationElement element)
    {
        var owner = host.Sessions.GetInputTarget(handle);
        var popup = await Task.Run(() =>
        {
            var menu = false;
            for (var current = element; current != null; current = current.Parent)
            {
                menu |= current.Properties.ControlType.ValueOrDefault == FlaUI.Core.Definitions.ControlType.Menu;
                if (current.Properties.ControlType.ValueOrDefault != FlaUI.Core.Definitions.ControlType.Window) continue;
                var hwnd = current.Properties.NativeWindowHandle.ValueOrDefault;
                if (hwnd != 0 && menu) return Win32Desktop.GetAncestor(hwnd, 2);
            }
            throw new InvalidOperationException("Command is not in the expected native menu");
        }).WaitAsync(TimeSpan.FromSeconds(5));
        if (popup == owner.Hwnd || Win32Desktop.GetProcessId(popup) != owner.ProcessId)
            throw new InvalidOperationException("Unexpected TE3 popup identity");
        return popup;
    }
    public async Task AssertLoaded(string objectName)
    {
        await Expect(ObjectTarget(objectName), objectName);
    }

    public async Task SelectObjectPath(string table, string objectName, string objectType, string folder = "")
    {
        if (objectType is not ("Table" or "Column" or "Measure")) throw new ArgumentException("Unsupported object type");
        await OpenTom(); await ResetSearch(); await CollapseObjectTree();
        await SelectObject("Tables"); await ExpandSelected(); await SelectObject(table);
        await AssertProperty("Name", table);
        if (objectType != "Table")
        {
            await ExpandSelected();
            foreach (var segment in folder.Split('\\', StringSplitOptions.RemoveEmptyEntries))
            { await SelectObject(segment); await ExpandSelected(); }
            // Without a folder, an object inside a display folder is simply absent; say so instead of a bare lookup miss.
            if (folder.Length == 0)
            {
                var row = ObjectTarget(objectName);
                var present = false;
                for (var attempt = 0; attempt < 4 && !present; attempt++)
                {
                    if (attempt > 0) await Task.Delay(500);
                    present = await Task.Run(() => query.IsPresent(handle, row.Selector, row.Within, row.IncludeOwned,
                        new SearchBudget(1000, TimeSpan.FromSeconds(2)))).WaitAsync(TimeSpan.FromSeconds(5));
                }
                if (!present) throw new InvalidOperationException(ObjectNotFoundMessage(table, objectName));
            }
            await SelectObject(objectName);
        }
        await AssertProperty("Name", objectType == "Table" ? table : objectName);
    }

    public static string ObjectNotFoundMessage(string table, string objectName) =>
        $"'{objectName}' is not directly under table '{table}'; if it is in a display folder, pass folder (e.g. Smoke tests).";

    /// <summary>Read-only arrival checks shared by companion navigation and capture.</summary>
    public async Task VerifyCompanionDestination(Te3Destination destination, string? table = null, string? objectName = null, string? objectType = null)
    {
        OperationContext.Check();
        switch (destination)
        {
            case { Section: { } section, PaneMarker: { } marker }:
                await Expect(new(new(AutomationId: "searchPreferences", ControlType: "Edit"), PreferencesTarget().Selector, true), "");
                var row = await Resolve(new(new(ControlType: "TreeItem", Value: section, Visible: true), new(AutomationId: "treePreferences"), true));
                if (!row.Patterns.SelectionItem.Pattern.IsSelected.Value) throw new InvalidOperationException("Expected Preferences row is not selected.");
                var control = await Resolve(new(new(Name: marker, ControlType: "CheckBox", Visible: true), new(AutomationId: "DAX Editor." + section), true));
                var dialog = await Resolve(PreferencesTarget());
                if (control.BoundingRectangle.IsEmpty || !dialog.BoundingRectangle.Contains(control.BoundingRectangle))
                    throw new InvalidOperationException("Expected pane content is not inside the visible Preferences dialog.");
                break;
            case { Id: "object" }:
                var name = (await Resolve(PropertyTarget("Name"))).Patterns.Value.Pattern.Value.Value;
                var type = (await Resolve(PropertyTarget("Object Type"))).Patterns.Value.Pattern.Value.Value;
                var dax = objectType == "Table" ? null : (await Resolve(PropertyTarget("DAX identifier"))).Patterns.Value.Pattern.Value.Value;
                VerifyObjectIdentity(table!, objectName!, objectType!, name, type, dax);
                break;
            case { Id: "tom-explorer" }:
                await RequireSelectedTab(TomTabTarget());
                break;
            case { Id: "expression-editor" }:
                await RequireSelectedTab(ExpressionTabTarget());
                break;
            default:
                throw new ArgumentException("No arrival check for destination " + destination.Id);
        }
    }

    private async Task RequireSelectedTab(Target target)
    {
        var tab = await Resolve(target);
        if (!tab.Patterns.SelectionItem.IsSupported || !tab.Patterns.SelectionItem.Pattern.IsSelected.Value)
            throw new InvalidOperationException("Expected view tab is not selected.");
    }

    internal static void VerifyObjectIdentity(string table, string objectName, string objectType, string? name, string? type, string? dax)
    {
        var actualType = type?.Replace(" ", "", StringComparison.Ordinal);
        var typeMatches = objectType == "Column" ? actualType is "Column" or "DataColumn" or "CalculatedColumn" or "CalculatedTableColumn"
            : objectType == "Table" ? actualType is "Table" or "CalculatedTable" : actualType == "Measure";
        var qualified = "'" + table.Replace("'", "''", StringComparison.Ordinal) + "'[" + objectName.Replace("]", "]]", StringComparison.Ordinal) + "]";
        var unquoted = table + "[" + objectName.Replace("]", "]]", StringComparison.Ordinal) + "]";
        if (!typeMatches || name != (objectType == "Table" ? table : objectName) || objectType != "Table" && dax != qualified && dax != unquoted)
            throw new InvalidOperationException($"Object identity unverified: expected {table}/{objectName} ({objectType}), observed {name}, {type}, {dax}. Inspect the property grid; no fallback guess was made.");
    }
}
