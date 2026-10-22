using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>
/// Each TE3 tool's facts (Core's ToolFacts: name, annotations, observation, recovery) build its tools/list entry and
/// the generated table in automation/README.md, and the names live in Te3ToolNames, so the server, the docs and the
/// code cannot disagree. Builds the server's registry; nothing is sent.
/// </summary>
public sealed class Te3ToolFactsTests : IDisposable
{
    private const string Begin = "<!-- te3-tool-facts:begin -->\n";
    private const string End = "<!-- te3-tool-facts:end -->";

    private readonly AutomationHost host = new(new ProcessPolicy(["TabularEditor3"]), includeDesktopTools: true);
    private readonly Te3Interaction interaction = new(() => 0);

    public Te3ToolFactsTests() => Te3McpServer.Install(host, interaction);

    public void Dispose()
    {
        MutationGuard.BeforeMutation.Value = null;
        interaction.Dispose();
        host.Dispose();
    }

    private Dictionary<string, ToolFacts> Facts() =>
        host
            .Tools.All.Cast<ToolBase>()
            .Select(tool => tool.Facts)
            .Where(facts => facts.Name.StartsWith("te3_", StringComparison.Ordinal))
            .ToDictionary(facts => facts.Name);

    [Fact]
    public void TheServerRegistersExactlyTheNamedTools() =>
        Assert.Equal(Te3ToolNames.All.Order(StringComparer.Ordinal), Facts().Keys.Order(StringComparer.Ordinal));

    [Fact]
    public void TheReadsAreObservationsAndTheGrantToolsWorkWhileTe3IsBlocked()
    {
        var facts = Facts();

        Assert.True(facts[Te3ToolNames.ModelState].Observation);
        Assert.True(facts[Te3ToolNames.LayoutRead].Observation);
        Assert.False(facts[Te3ToolNames.Layout].Observation);
        Assert.False(facts[Te3ToolNames.Navigate].Observation);
        Assert.True(facts[Te3ToolNames.InteractionBegin].Recovery);
        Assert.True(facts[Te3ToolNames.InteractionEnd].Recovery);
        Assert.True(facts[Te3ToolNames.EnvCheck].Recovery);
        Assert.False(facts[Te3ToolNames.Navigate].InputFree);
        Assert.True(facts[Te3ToolNames.ModelState].InputFree);
    }

    [Fact]
    public void TheReadmeTableIsGeneratedFromTheFacts()
    {
        var path = Path.Combine(DirectInputJournalTests.Sources(), "..", "..", "automation", "README.md");
        var text = File.ReadAllText(path).ReplaceLineEndings("\n");
        var start = text.IndexOf(Begin, StringComparison.Ordinal);
        var end = text.IndexOf(End, StringComparison.Ordinal);
        var generated = ToolFacts.ReadmeTable(Facts().Values);
        if (Environment.GetEnvironmentVariable("FLAUI_APPROVE_TOOL_SCHEMAS") == "1" && start >= 0 && end > start)
        {
            File.WriteAllText(path, text[..(start + Begin.Length)] + generated + text[end..]);
            return;
        }

        Assert.True(start >= 0 && end > start, "automation/README.md has no te3-tool-facts block.");
        Assert.Equal(generated, text[(start + Begin.Length)..end]);
    }
}
