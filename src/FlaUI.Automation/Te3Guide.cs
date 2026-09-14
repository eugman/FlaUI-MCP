using System.Security.Cryptography;
using System.Text;
using PlaywrightWindows.Mcp;

/// <summary>One set of embedded Markdown topics serves tools, resources, and local readers.</summary>
public static class Te3Guide
{
    public static readonly IReadOnlyDictionary<string, McpTextResource> Topics = Load();
    // A hash of the topic content, so an edited map can never keep an old revision.
    public static readonly string Revision = HashTopics(Topics);

    private static IReadOnlyDictionary<string, McpTextResource> Load()
    {
        var assembly = typeof(Te3Guide).Assembly;
        var topics = new Dictionary<string, McpTextResource>(StringComparer.Ordinal);
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("te3-map/", StringComparison.Ordinal)).Order())
        {
            using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
            var topic = name["te3-map/".Length..^3];
            var text = reader.ReadToEnd();
            topics.Add(topic, new("te3://map/" + topic, text.Split('\n')[0].Trim('#', ' ', '\r'), text));
        }
        return topics;
    }

    internal static string HashTopics(IReadOnlyDictionary<string, McpTextResource> topics)
    {
        var text = string.Concat(topics.OrderBy(t => t.Key, StringComparer.Ordinal).Select(t => t.Key + "\n" + t.Value.Text + "\n"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..12].ToLowerInvariant();
    }

    public static object Read(string? topic) => topic == null
        ? new { revision = Revision, topics = Topics.Select(t => new { topic = t.Key, t.Value.Name, t.Value.Uri }) }
        : Topics.TryGetValue(topic, out var resource)
            ? new { revision = Revision, topic, resource.Uri, content = resource.Text }
            : throw new ArgumentException("Unknown topic. Call te3_catalog without topic for the supported index.");
}
