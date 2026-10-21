using System.Text.Json;
using System.Text.RegularExpressions;
using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// Every argument a core tool's handler reads is declared in its schema, every declared argument is read, and
/// the schema closes with additionalProperties: false, so an agent can neither miss an option (as it missed
/// windows_fill's verifyFocus) nor send one that is silently ignored. A source scan of each tool class.
/// </summary>
[Collection("Native host construction")]
public sealed partial class SchemaParityTests
{
    [GeneratedRegex("""Get(?:String|Bool|Int)?Argument(?:<.+?>)?\(\s*\w+\s*,\s*"(\w+)"\s*[,)]""")]
    private static partial Regex ArgumentRead();

    // A declared argument may be read through a local helper (Limit("maxDepth", 24), ResolvePoint(arguments,
    // "from", ...)), so the reverse check accepts the name as any string literal in the class.
    [GeneratedRegex("""(?<![\w$])"(\w+)"(?=\s*[,)])""")]
    private static partial Regex PropertyRead();

    [GeneratedRegex(
        @"^(?:public |internal )?(?:sealed |static |partial |abstract )*class (\w+)",
        RegexOptions.Multiline
    )]
    private static partial Regex ClassDeclaration();

    private static string ToolsDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var tools = Path.Combine(dir.FullName, "src", "FlaUI.Mcp", "Tools");
            if (Directory.Exists(tools))
            {
                return tools;
            }
        }

        throw new DirectoryNotFoundException("src/FlaUI.Mcp/Tools");
    }

    /// <summary>The source of a class, across every file (and partial part) that declares it.</summary>
    private static string ClassSource(string name)
    {
        var parts = new List<string>();
        foreach (var file in Directory.GetFiles(ToolsDirectory(), "*.cs"))
        {
            var text = File.ReadAllText(file);
            var declarations = ClassDeclaration().Matches(text).ToList();
            for (var i = 0; i < declarations.Count; i++)
            {
                if (declarations[i].Groups[1].Value == name)
                {
                    var end = i + 1 < declarations.Count ? declarations[i + 1].Index : text.Length;
                    parts.Add(text[declarations[i].Index..end]);
                }
            }
        }

        return string.Join("\n", parts);
    }

    public static TheoryData<string> ToolNames()
    {
        using var host = new AutomationHost(new ProcessPolicy(["test-app"]), includeDesktopTools: true);
        return new(host.Tools.All.Select(tool => tool.Name).Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(ToolNames))]
    public void TheSchemaDeclaresExactlyTheArgumentsTheHandlerReads(string name)
    {
        using var host = new AutomationHost(new ProcessPolicy(["test-app"]), includeDesktopTools: true);
        var tool = host.Tools.All.Single(t => t.Name == name);
        var schema = JsonSerializer.SerializeToElement(((ToolBase)tool).Facts.Schema, McpProtocol.JsonOptions);
        var declared = schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToHashSet();
        var source = ClassSource(tool.GetType().Name);
        Assert.False(string.IsNullOrEmpty(source), $"No source found for {tool.GetType().Name}");
        var read = ArgumentRead().Matches(source).Select(m => m.Groups[1].Value).ToHashSet();
        // FocusRules.Parse reads verifyFocus (true, false or "self-or-descendant") for the tool.
        if (
            source.Contains("FocusRules.Parse(", StringComparison.Ordinal)
            || source.Contains("KeyboardVerifiesFocus(arguments)", StringComparison.Ordinal)
        )
        {
            read.Add("verifyFocus");
        }

        var touched = read.Union(PropertyRead().Matches(source).Select(m => m.Groups[1].Value)).ToHashSet();

        Assert.True(schema.TryGetProperty("additionalProperties", out var closed), $"{name} schema is open");
        Assert.Equal(JsonValueKind.False, closed.ValueKind);
        Assert.Empty(read.Except(declared).Order(StringComparer.Ordinal));
        Assert.Empty(declared.Except(touched).Order(StringComparer.Ordinal));
    }
}
