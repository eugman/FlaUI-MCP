using System.Diagnostics;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Mcp.Core;

public sealed partial class Te3Page
{
    private static Target SerializationModeTarget() =>
        new(
            new(Name: "Serialization mode", ControlType: "ComboBox"),
            new(AutomationId: "File Formats.Save-to-folder"),
            true
        );

    public Task OpenSerializationModes()
    {
        MarkPopups();
        return Keys("Alt+Down", SerializationModeTarget());
    }

    public Task CloseSerializationModes() => Keys("Escape", SerializationModeTarget());

    public static void VerifyLanguageChoices(IReadOnlyList<ElementInfo> items)
    {
        // TE3 3.26.3 capitalizes Spanish/French; historical docs used lowercase.
        string[] expected =
        [
            "English",
            "Deutsch (Beta)",
            "Español (Preview)",
            "Français (Beta)",
            "中文(简体) (Preview)",
            "日本語 (Beta)",
        ];
        if (
            !items.Select(e => e.Name).SequenceEqual(expected)
            || items.Any(e => e.Offscreen)
            || items.Count(e => e.Selected == true) != 1
            || items[0].Selected != true
        )
        {
            throw new InvalidOperationException("Language choices differ from verified TE3 3.26.3; inspect popup map");
        }
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
        foreach (
            var name in new[]
            {
                "Serialize relationships on from-tables",
                "Serialize perspective membership info on objects",
                "Serialize translations on translated objects",
            }
        )
        {
            var target = new Target(new(Name: name, ControlType: "CheckBox", Visible: true), pane, true);
            var element = await Resolve(target);
            var state = await Task.Run(() => element.Patterns.Toggle.Pattern.ToggleState.Value)
                .WaitAsync(TimeSpan.FromSeconds(5));
            if (state != FlaUI.Core.Definitions.ToggleState.Off)
            {
                throw new InvalidOperationException("Unexpected initial checkbox: " + name);
            }

            await Click(target);
            element = await Resolve(target);
            state = await Task.Run(() => element.Patterns.Toggle.Pattern.ToggleState.Value)
                .WaitAsync(TimeSpan.FromSeconds(5));
            if (state != FlaUI.Core.Definitions.ToggleState.On)
            {
                throw new InvalidOperationException("Checkbox did not turn on: " + name);
            }
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
        if (
            !await Task.Run(() => selected.Patterns.SelectionItem.Pattern.IsSelected.Value)
                .WaitAsync(TimeSpan.FromSeconds(5))
        )
        {
            throw new InvalidOperationException("Data Sources must be selected");
        }

        var prefix = await Resolve(
            new(new(Name: "Prefix file names sequentially", ControlType: "CheckBox"), pane, true)
        );
        if (
            await Task.Run(() => prefix.Patterns.Toggle.Pattern.ToggleState.Value).WaitAsync(TimeSpan.FromSeconds(5))
            != FlaUI.Core.Definitions.ToggleState.Off
        )
        {
            throw new InvalidOperationException("Sequential filename prefix must be off");
        }

        foreach (
            var value in new[] { "Data Sources", "Shared Expressions", "Roles", "Tables", "Columns", "Hierarchies" }
        )
        {
            var row = await Resolve(new(new(Value: value, ControlType: "TreeItem", Visible: true), tree, true));
            // DevExpress exposes checked tree rows through MSAA STATE_SYSTEM_CHECKED, not UIA Toggle.
            var checkedState = await Task.Run(() =>
                    row.Patterns.LegacyIAccessible.IsSupported
                        ? (uint)row.Patterns.LegacyIAccessible.Pattern.State.Value
                        : 0
                )
                .WaitAsync(TimeSpan.FromSeconds(5));
            if ((checkedState & 0x10) == 0)
            {
                throw new InvalidOperationException("Serialization row not checked via accessibility: " + value);
            }
        }
    }

    public async Task SaveSerializationModes(string path)
    {
        var owner = host.Sessions.GetInputTarget(handle);
        string[] expected = ["Database.json (default)", "Database.json (customizable)", "TMDL"];
        // Alt+Down can return before the dropdown window exists, so poll until one popup holds the choices.
        var clock = Stopwatch.StartNew();
        while (true)
        {
            var popups = await ObservePopups(owner.ProcessId, maxWindows: 6);
            var lists = popups.Where(p => p.Tree.Elements.Any(e => e.ControlType == "ListItem")).ToArray();
            var problem = "Serialization mode popup not found";
            if (lists.Length == 1 && lists[0].Tree.Complete)
            {
                var items = lists[0].Tree.Elements.Where(e => e.ControlType == "ListItem").ToArray();
                if (
                    items.Select(e => e.Name).SequenceEqual(expected)
                    && items.Count(e => e.Selected == true) == 1
                    && items[0].Selected == true
                )
                {
                    File.WriteAllText(path, JsonSerializer.Serialize(popups, Indented));
                    return;
                }

                problem = "Serialization mode popup differs from observed choices";
            }

            if (clock.Elapsed > TimeSpan.FromSeconds(5))
            {
                throw new InvalidOperationException(problem);
            }

            await Task.Delay(100);
        }
    }
}
