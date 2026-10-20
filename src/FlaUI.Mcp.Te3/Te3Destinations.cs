/// <summary>A place the companion can navigate to and verify.</summary>
public sealed record Te3Destination(
    string Id,
    string? Section = null,
    string? PaneMarker = null,
    string? PaneAutomationId = null,
    string PaneMarkerControlType = "CheckBox"
);

/// <summary>The one destination list behind the companion's schemas, validation, navigation and arrival checks.</summary>
public static class Te3Destinations
{
    public static readonly IReadOnlyList<Te3Destination> All =
    [
        new("tom-explorer"),
        new("tom-explorer/changes"),
        new("native-mcp-server"),
        new("expression-editor"),
        new("object"),
        // Preferences destinations name a tree section and a pane control that proves the page is showing.
        new(
            "preferences/auto-formatting",
            "Auto Formatting",
            "Auto format code as you type",
            "DAX Editor.Auto Formatting"
        ),
        new("preferences/code-actions", "Code Actions", "Show code actions", "DAX Editor.Code Actions"),
        new("preferences/ai-provider", "AI Provider", "Choose provider", "AI Assistant.AI Provider", "ComboBox"),
        new(
            "preferences/ai-assistant",
            "Preferences",
            "Allow AI assistant to run C# scripts directly",
            "AI Assistant.Preferences"
        ),
        new("preferences/mcp-server", "MCP Server", "Enable MCP Server", "AI Features.MCP Server"),
        new("preferences/permissions", "Permissions", "Model metadata:", "AI Features.Permissions", "ComboBox"),
    ];
    public static readonly string[] Ids = All.Select(d => d.Id).ToArray();
    public static readonly string[] ObjectTypes = ["Table", "Column", "Measure"];

    public static Te3Destination? Find(string? id) => All.FirstOrDefault(d => d.Id == id);
}
