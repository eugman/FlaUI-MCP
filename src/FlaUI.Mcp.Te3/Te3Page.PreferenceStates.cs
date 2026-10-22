using System.Diagnostics;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Mcp.Core;

public sealed partial class Te3Page
{
    public async Task AssertAutoFormattingState()
    {
        var pane = new ElementSelector(AutomationId: "DAX Editor.Auto Formatting");
        await AssertCheckboxes(
            pane,
            visibleOnly: false,
            ("Auto format code as you type", true),
            ("Auto-format function calls", true),
            ("Auto-indent", true),
            ("Auto-brace", true),
            ("Wrap selection", true),
            ("Space after functions", false),
            ("Newline after functions", false),
            ("Newline before operator", true),
            ("Pad parentheses", true),
            ("Fix measure/column qualifiers", true),
            ("Fix keyword/function casing", true),
            ("Fix object reference casing", true),
            ("Always quote tables", false),
            ("Always prefix extension columns", false)
        );
        await Expect(
            new(new(Name: "Long format line limit", ControlType: "Spinner", Pattern: "Value"), pane, true),
            "120"
        );
        await Expect(
            new(new(Name: "Short format line limit", ControlType: "Spinner", Pattern: "Value"), pane, true),
            "60"
        );
        await Expect(new(new(Name: "Preferred keyword casing", ControlType: "ComboBox"), pane, true), "Default");
        await Expect(new(new(Name: "Preferred function casing", ControlType: "ComboBox"), pane, true), "Default");
        await Expect(
            new(new(Name: "Use default formatting settings", ControlType: "Button", Visible: true), pane, true)
        );
    }

    public async Task AssertFileFormatsState()
    {
        var pane = new ElementSelector(AutomationId: "File Formats.General");
        await Expect(new(pane, IncludeOwned: true));
        await AssertCheckboxes(
            pane,
            visibleOnly: true,
            ("Ignore inferred objects", true),
            ("Ignore inferred properties", true),
            ("Ignore timestamps", true),
            ("Ignore lineage tags", false),
            ("Ignore privacy settings", false),
            ("Split multiline strings", true),
            ("Sort arrays by name", false),
            ("Include sensitive", false),
            ("Ignore incremental refresh partitions", false),
            ("Use PBIX filename as database name when serializing", true),
            ("Use latest default", true),
            ("Use workspace database", true),
            ("Create user options (.tmuo) file", true)
        );
        await Expect(
            new(new(Name: "Default save format", ControlType: "ComboBox", Visible: true), pane, true),
            "Always ask"
        );
        // TE3 3.26 uses 1600 here; the historical docs show 1500. Do not change the user's default.
        var compatibility = new Target(
            new(Name: "Compatibility level", ControlType: "ComboBox", Visible: true),
            pane,
            true
        );
        await Expect(compatibility, "1600 (Azure Analysis Services / SQL Server 2022+)");
        var element = await Resolve(compatibility);
        if (await Task.Run(() => element.Properties.IsEnabled.Value).WaitAsync(TimeSpan.FromSeconds(5)))
        {
            throw new InvalidOperationException("Default compatibility level must be disabled");
        }
    }

    // Compares each checkbox with its expected original state; preferences are never changed to match.
    private async Task AssertCheckboxes(
        ElementSelector pane,
        bool visibleOnly,
        params (string Name, bool On)[] expected
    )
    {
        foreach (var (name, on) in expected)
        {
            var control = await Resolve(
                new(new(Name: name, ControlType: "CheckBox", Visible: visibleOnly ? true : null), pane, true)
            );
            var state = await Task.Run(() => control.Patterns.Toggle.Pattern.ToggleState.Value)
                .WaitAsync(TimeSpan.FromSeconds(5));
            if (state != (on ? FlaUI.Core.Definitions.ToggleState.On : FlaUI.Core.Definitions.ToggleState.Off))
            {
                throw new InvalidOperationException(
                    $"Checkbox differs from its expected state ({(on ? "on" : "off")}): {name}"
                );
            }
        }
    }

    public async Task AssertDaxGeneralState()
    {
        var pane = new ElementSelector(AutomationId: "DAX Editor.General");
        await Expect(new(pane, IncludeOwned: true));
        await AssertCheckboxes(
            pane,
            visibleOnly: true,
            ("Line numbers", true),
            ("Code folding", true),
            ("Visible whitespace", false),
            ("Indentation guides", true),
            ("Use tabs", false),
            ("Use daxformatter.com instead of built-in formatter", false)
        );
        foreach (
            var (name, value) in new[]
            {
                ("Comment style:", "Slashes"),
                ("Locale", "US (A, B, C, 1234.00)"),
                ("Semantic engine", "Auto-detect"),
                ("Scalar predicates", "Auto-detect"),
                ("Directional CF", "Auto-detect"),
                ("Date Literals", "Auto-detect"),
                ("Measure/Agg Shortcut", "Auto-detect"),
                ("Table-named variables", "Auto-detect"),
                ("Default DAX formatting", "Long lines"),
                ("Documentation URL", "https://dax.guide/{0}"),
            }
        )
        {
            await Expect(
                new(new(Name: name, ControlType: "ComboBox", Pattern: "Value", Visible: true), pane, true),
                value
            );
        }

        await Expect(
            new(new(Name: "Tab/indent width", ControlType: "Spinner", Pattern: "Value", Visible: true), pane, true),
            "4"
        );
        await Expect(
            new(new(Name: "Request Timeout (ms)", ControlType: "Spinner", Pattern: "Value", Visible: true), pane, true),
            "5000"
        );
    }

    public async Task AssertCodeActionsState()
    {
        var rows = await Task.Run(() =>
                query.Find(
                    handle,
                    new(ControlType: "TreeItem", Visible: true),
                    new(AutomationId: "treePreferences"),
                    maxResults: 100,
                    includeOwned: true,
                    budget: new SearchBudget(1000, TimeSpan.FromSeconds(3))
                )
            )
            .WaitAsync(TimeSpan.FromSeconds(5));
        if (
            rows.Truncated
            || rows.Unreadable != 0
            || rows.Elements.FirstOrDefault()?.Value != "Pivot Grid"
            || !rows.Elements.Any(row => row.Value == "Model Deployment")
        )
        {
            throw new InvalidOperationException(
                "Code Actions tree framing must start at Pivot Grid and include Model Deployment"
            );
        }

        var pane = new ElementSelector(AutomationId: "DAX Editor.Code Actions");
        await AssertCheckboxes(pane, visibleOnly: false, ("Show code actions", true), ("Apply variable casing", false));
        await Expect(new(new(Name: "Variable prefix", ControlType: "ComboBox"), pane, true), "_");
        await Expect(new(new(Name: "Extension column prefix", ControlType: "ComboBox"), pane, true), "@");
        var casing = new Target(new(Name: "Preferred variable casing", ControlType: "ComboBox"), pane, true);
        await Expect(casing, "Default");
        var element = await Resolve(casing);
        if (await Task.Run(() => element.Properties.IsEnabled.Value).WaitAsync(TimeSpan.FromSeconds(5)))
        {
            throw new InvalidOperationException("Preferred variable casing must be disabled");
        }
    }
}
