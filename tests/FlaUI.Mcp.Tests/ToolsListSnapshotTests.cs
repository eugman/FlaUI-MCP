using System.Text.Json;
using System.Text.Json.Nodes;
using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// The core's tools/list output, pinned. tools-list.json is the exact output; tools-list.baseline.json is the output
/// from before the schemas were generated from argument records, and the live output may differ from it only by
/// argument descriptions it adds. Set FLAUI_WRITE_TOOLS_SNAPSHOT=1 to rewrite tools-list.json after a deliberate
/// change, then review its diff.
/// </summary>
[Collection("Native host construction")]
public sealed class ToolsListSnapshotTests
{
    private static string SnapshotPath(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var folder = Path.Combine(dir.FullName, "tests", "FlaUI.Mcp.Tests", "Snapshots");
            if (Directory.Exists(folder))
            {
                return Path.Combine(folder, name);
            }
        }

        throw new DirectoryNotFoundException("tests/FlaUI.Mcp.Tests/Snapshots");
    }

    private static string ToolsList()
    {
        using var host = new AutomationHost(new ProcessPolicy(["test-app"]), includeDesktopTools: true);
        var tools = host.Tools.GetToolDefinitions().OrderBy(t => t.Name, StringComparer.Ordinal).ToList();
        var options = new JsonSerializerOptions(McpProtocol.JsonOptions) { WriteIndented = true };
        return JsonSerializer.Serialize(tools, options).ReplaceLineEndings("\n") + "\n";
    }

    private static string Read(string name) => File.ReadAllText(SnapshotPath(name)).ReplaceLineEndings("\n");

    [Fact]
    public void ToolsListMatchesItsSnapshot()
    {
        var live = ToolsList();
        if (Environment.GetEnvironmentVariable("FLAUI_WRITE_TOOLS_SNAPSHOT") == "1")
        {
            File.WriteAllText(SnapshotPath("tools-list.json"), live);
        }

        Assert.Equal(Read("tools-list.json"), live);
    }

    [Fact]
    public void ToolsListDiffersFromTheBaselineOnlyByDescriptionsAndDocumentedAdditions()
    {
        var baseline = JsonNode.Parse(Read("tools-list.baseline.json"))!;
        var live = JsonNode.Parse(ToolsList())!;
        var kept = new List<string>();
        var mouse = live.AsArray().Single(tool => tool!["name"]!.GetValue<string>() == "windows_mouse")!;
        var properties = mouse["inputSchema"]!["properties"]!.AsObject();
        // The right-button click is an intended new argument and tool description.
        Assert.Equal("string", properties["button"]!["type"]!.GetValue<string>());
        Assert.True(properties.Remove("button"));
        mouse["description"] = baseline.AsArray().Single(tool => tool!["name"]!.GetValue<string>() == "windows_mouse")![
            "description"
        ]!.DeepClone();
        foreach (var guard in new[] { "fromMustHit", "toMustHit" })
        {
            Assert.Equal("string", properties[guard]!["type"]!.GetValue<string>());
            Assert.True(properties.Remove(guard));
        }

        var launch = live.AsArray().Single(tool => tool!["name"]!.GetValue<string>() == "windows_launch")!;
        Assert.Contains("provisional", launch["description"]!.GetValue<string>(), StringComparison.Ordinal);
        launch["description"] = baseline
            .AsArray()
            .Single(tool => tool!["name"]!.GetValue<string>() == "windows_launch")!["description"]!.DeepClone();
        var launchProperties = launch["inputSchema"]!["properties"]!.AsObject();
        Assert.Equal("integer", launchProperties["waitSeconds"]!["type"]!.GetValue<string>());
        Assert.True(launchProperties.Remove("waitSeconds"));

        Assert.Equal(Strip(baseline, "", kept).ToJsonString(), Strip(live, "", []).ToJsonString());
        // Every description the baseline had is still there, word for word.
        var liveDescriptions = new List<string>();
        Strip(JsonNode.Parse(ToolsList())!, "", liveDescriptions);
        Assert.Empty(kept.Except(liveDescriptions));
    }

    /// <summary>The node with every argument description removed; the removed ones are listed with their paths.</summary>
    private static JsonNode Strip(JsonNode node, string path, List<string> removed)
    {
        if (node is JsonObject obj)
        {
            var inProperties = path.EndsWith("/properties/" + path.Split('/')[^1], StringComparison.Ordinal);
            if (inProperties && obj["description"] is { } description)
            {
                removed.Add($"{path}: {description.ToJsonString()}");
                obj.Remove("description");
            }

            foreach (var (key, child) in obj.ToList())
            {
                if (child != null)
                {
                    Strip(child, $"{path}/{key}", removed);
                }
            }
        }
        else if (node is JsonArray array)
        {
            for (var i = 0; i < array.Count; i++)
            {
                if (array[i] is { } item)
                {
                    Strip(item, $"{path}/{i}", removed);
                }
            }
        }

        return node;
    }
}
