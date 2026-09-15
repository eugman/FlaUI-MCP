using System.Text.Json;
using System.Text.RegularExpressions;

public sealed record BacklogItem(string Id, string Source, string[] Pages, string Status, string? Recipe);

/// <summary>Reads automation/screenshot-backlog.json so checkpoints named after an item id can find their original.</summary>
public static partial class Backlog
{
    public static bool IsItemId(string checkpoint) => ItemId().IsMatch(checkpoint);

    public static string DefaultPath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "automation", "screenshot-backlog.json");
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("automation/screenshot-backlog.json not found above the runner directory");
    }

    public static BacklogItem Find(string id, string? path = null)
    {
        var items = JsonSerializer.Deserialize<BacklogItem[]>(File.ReadAllText(path ?? DefaultPath()), RunConfig.Json)
            ?? throw new JsonException("Empty backlog");
        return items.SingleOrDefault(item => item.Id == id) ?? throw new ArgumentException("Unknown backlog item: " + id);
    }

    /// <summary>The docs-relative PNG an item replaces, or an error saying why it needs an explicit destination.</summary>
    public static string DocsImage(BacklogItem item)
    {
        if (!item.Source.StartsWith("content/", StringComparison.Ordinal))
            throw new ArgumentException($"{item.Id} is hosted outside the docs ({item.Source}); promote with an explicit destination");
        if (!item.Source.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"{item.Id} replaces a non-PNG image ({item.Source}); promote with an explicit destination");
        return item.Source;
    }

    [GeneratedRegex("^[LR][0-9]{3}$")]
    private static partial Regex ItemId();
}
