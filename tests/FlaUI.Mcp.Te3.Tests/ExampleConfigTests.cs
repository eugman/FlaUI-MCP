using System.Text.Json;
using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>automation/example.config.json repeats the smoke recipes by hand; this keeps the copy from drifting.</summary>
public sealed class ExampleConfigTests
{
    [Fact]
    public void TheExampleConfigListsTheSmokeRecipes()
    {
        var repository = Path.GetFullPath(Path.Combine(DirectInputJournalTests.Sources(), "..", ".."));
        var example = File.ReadAllText(Path.Combine(repository, "automation", "example.config.json"));

        var scenarios = JsonDocument
            .Parse(example)
            .RootElement.GetProperty("scenarios")
            .EnumerateArray()
            .Select(scenario => scenario.GetString());

        Assert.Equal(RecipeCatalog.SmokeIds, scenarios);
    }
}
