using System.Text.Json;
using FlaUI.Mcp;
using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// The name, description, input schema and annotations of each input tool are what an agent reads before
/// it acts, so a change to them must be deliberate. Each is compared with an approved copy in ToolSchemas.
/// After an intended change, run the tests with FLAUI_APPROVE_TOOL_SCHEMAS=1 to rewrite the copies, and
/// review the diff.
/// </summary>
public sealed class ToolSchemaTests
{
    private static readonly JsonSerializerOptions Indented = new(McpProtocol.JsonOptions) { WriteIndented = true };

    public static TheoryData<string> Tools() => new(Definitions().Keys);

    private static Dictionary<string, ITool> Definitions()
    {
        var refs = new ElementRegistry();
        var pending = new PendingInvokeTracker();
        var sessions = new SessionManager(null, ProcessPolicy.AllowAll);
        var registry = new ToolRegistry();
        ITool[] tools =
        [
            new ClickTool(refs, pending),
            new MouseTool(refs, pending, sessions),
            new SendKeysTool(refs, pending),
            new TypeTool(refs, pending),
            new FillTool(refs, pending),
            new BatchTool(registry),
        ];
        return tools.ToDictionary(tool => tool.Name);
    }

    private static string ApprovedPath(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var project = Path.Combine(dir.FullName, "tests", "FlaUI.Mcp.Tests");
            if (Directory.Exists(project))
            {
                return Path.Combine(project, "ToolSchemas", $"{name}.json");
            }
        }

        throw new DirectoryNotFoundException("tests/FlaUI.Mcp.Tests");
    }

    [Theory]
    [MemberData(nameof(Tools))]
    public void TheToolDefinitionMatchesItsApprovedCopy(string name)
    {
        using var offlineDesktop = new FakeDesktop().Install();
        var actual = JsonSerializer.Serialize(Definitions()[name].GetDefinition(), Indented) + "\n";
        var path = ApprovedPath(name);
        if (Environment.GetEnvironmentVariable("FLAUI_APPROVE_TOOL_SCHEMAS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual);
        }

        Assert.True(File.Exists(path), $"No approved copy at {path}; run with FLAUI_APPROVE_TOOL_SCHEMAS=1.");
        Assert.Equal(File.ReadAllText(path).ReplaceLineEndings("\n"), actual.ReplaceLineEndings("\n"));
    }
}
