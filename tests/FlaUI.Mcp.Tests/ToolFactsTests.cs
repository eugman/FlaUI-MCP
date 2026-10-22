using System.Text.Json;
using FlaUI.Mcp;
using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// Each tool's facts live in one record (name, description, annotations including observation, recovery and
/// mutating-when-argument, the schema, inputFree), which generates the tools/list entry and the README table, so
/// the hints a broker reads, the docs and the code cannot disagree.
/// </summary>
[Collection("Native host construction")]
public sealed class ToolFactsTests
{
    private static Dictionary<string, ToolFacts> Facts()
    {
        using var host = new AutomationHost(new ProcessPolicy(["test-app"]), includeDesktopTools: true);
        return host.Tools.All.Cast<ToolBase>().Select(tool => tool.Facts).ToDictionary(facts => facts.Name);
    }

    [Fact]
    public void ScreenshotMutatesOnlyWhenItParksThePointer()
    {
        var screenshot = Facts()["windows_screenshot"];

        Assert.Equal(["parkPointer"], screenshot.MutatingWhenArgument);
        Assert.True(screenshot.Observation);
        var annotations = JsonSerializer.SerializeToElement(screenshot.Annotations, McpProtocol.JsonOptions);
        Assert.Equal("parkPointer", annotations.GetProperty("mutatingWhenArgument")[0].GetString());
        Assert.True(annotations.GetProperty("observationHint").GetBoolean());
    }

    [Fact]
    public void ReadOnlyToolsAreInputFreeAndInputToolsAreNot()
    {
        var facts = Facts();

        Assert.True(facts["windows_find"].InputFree);
        Assert.True(facts["windows_find"].Observation);
        Assert.False(facts["windows_click"].InputFree);
        Assert.True(facts["windows_operation_status"].Recovery);
        Assert.True(facts["windows_close"].Recovery);
    }

    [Fact]
    public void TheDefinitionIsBuiltFromTheFacts()
    {
        foreach (var facts in Facts().Values)
        {
            Assert.Equal(
                JsonSerializer.Serialize(facts.Schema, McpProtocol.JsonOptions),
                JsonSerializer.Serialize(facts.Definition().InputSchema, McpProtocol.JsonOptions)
            );
        }
    }

    [Fact]
    public void TheReadmeTableIsGeneratedFromTheFacts()
    {
        var readme = File.ReadAllText(ReadmePath());
        const string Begin = "<!-- tool-facts:begin -->\n";
        const string End = "<!-- tool-facts:end -->";
        var text = readme.ReplaceLineEndings("\n");
        var start = text.IndexOf(Begin, StringComparison.Ordinal);
        var end = text.IndexOf(End, StringComparison.Ordinal);
        var generated = ToolFacts.ReadmeTable(Facts().Values);
        if (Environment.GetEnvironmentVariable("FLAUI_APPROVE_TOOL_SCHEMAS") == "1" && start >= 0 && end > start)
        {
            File.WriteAllText(ReadmePath(), text[..(start + Begin.Length)] + generated + text[end..]);
            return;
        }

        Assert.True(start >= 0 && end > start, "README has no tool-facts block.");
        Assert.Equal(generated, text[(start + Begin.Length)..end]);
    }

    [Fact]
    public void ADocumentedArgumentRecordGeneratesItsSchema()
    {
        var schema = JsonSerializer.SerializeToElement(ToolSchema.For<SampleArguments>(), McpProtocol.JsonOptions);

        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        var properties = schema.GetProperty("properties");
        Assert.Equal("string", properties.GetProperty("ref").GetProperty("type").GetString());
        Assert.Equal("The element ref.", properties.GetProperty("ref").GetProperty("description").GetString());
        Assert.Equal("integer", properties.GetProperty("count").GetProperty("type").GetString());
        Assert.Equal("boolean", properties.GetProperty("submit").GetProperty("type").GetString());
        Assert.Equal(["ref"], schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()));
    }

    private sealed record SampleArguments(
        [property: ToolDoc("The element ref.")] string Ref,
        [property: ToolDoc("How many.")] int? Count = null,
        [property: ToolDoc("Press Enter.")] bool Submit = false
    );

    private static string ReadmePath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "README.md");
            if (File.Exists(path) && Directory.Exists(Path.Combine(dir.FullName, "src", "FlaUI.Mcp")))
            {
                return path;
            }
        }

        throw new FileNotFoundException("README.md");
    }
}
