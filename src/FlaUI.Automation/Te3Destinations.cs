/// <summary>A place the companion can navigate to and verify.</summary>
public sealed record Te3Destination(string Id, string? Section = null, string? PaneMarker = null);

/// <summary>The one destination list behind the companion's schemas, validation, navigation and arrival checks.</summary>
public static class Te3Destinations
{
    public static readonly IReadOnlyList<Te3Destination> All =
    [
        new("tom-explorer"),
        new("expression-editor"),
        new("object"),
        // Preferences destinations name their tree section and a checkbox that proves the pane is showing.
        new("preferences/auto-formatting", "Auto Formatting", "Auto format code as you type"),
        new("preferences/code-actions", "Code Actions", "Show code actions")
    ];
    public static readonly string[] Ids = All.Select(d => d.Id).ToArray();
    public static readonly string[] ObjectTypes = ["Table", "Column", "Measure"];

    public static Te3Destination? Find(string? id) => All.FirstOrDefault(d => d.Id == id);
}
