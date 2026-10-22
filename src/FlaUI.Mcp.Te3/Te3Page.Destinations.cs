using System.Diagnostics;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Mcp.Core;

public sealed partial class Te3Page
{
    public async Task AssertLoaded(string objectName)
    {
        await Expect(ObjectTarget(objectName), objectName);
    }

    private async Task RequireSelectedTab(Target target)
    {
        var tab = await Resolve(target);
        if (!tab.Patterns.SelectionItem.IsSupported || !tab.Patterns.SelectionItem.Pattern.IsSelected.Value)
        {
            throw new InvalidOperationException("Expected view tab is not selected.");
        }
    }
}
