using System.Text.RegularExpressions;
using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>The README's annotation table lists every core tool under the hints the code gives it.</summary>
[Collection("Native host construction")]
public sealed partial class ReadmeAnnotationTests
{
    [GeneratedRegex("`([a-z_]+)`")]
    private static partial Regex ToolName();

    private static string Readme()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "README.md");
            if (File.Exists(path) && Directory.Exists(Path.Combine(dir.FullName, "src", "FlaUI.Mcp")))
            {
                return File.ReadAllText(path);
            }
        }

        throw new FileNotFoundException("README.md");
    }

    /// <summary>The class each tool is listed under in the README table, by its name without "windows_".</summary>
    private static Dictionary<string, string> Listed()
    {
        var listed = new Dictionary<string, string>();
        foreach (
            var row in Readme().Split('\n').Where(line => line.StartsWith("| `readOnlyHint", StringComparison.Ordinal))
        )
        {
            var cells = row.Split('|', StringSplitOptions.TrimEntries);
            var hint = cells[1];
            var parts = cells[2].Split("; also", 2);
            var plain = Class(
                new McpToolAnnotations
                {
                    ReadOnlyHint = hint.Contains("readOnlyHint: true"),
                    DestructiveHint = hint.Contains("destructiveHint: true"),
                }
            );
            foreach (Match name in ToolName().Matches(parts[0]))
            {
                listed[name.Groups[1].Value] = plain;
            }

            foreach (Match name in parts.Length > 1 ? ToolName().Matches(parts[1]) : Enumerable.Empty<Match>())
            {
                listed[name.Groups[1].Value] = "idempotent input";
            }
        }

        return listed;
    }

    private static string Class(McpToolAnnotations annotations)
    {
        if (annotations.ReadOnlyHint == true)
        {
            return "read-only";
        }

        if (annotations.DestructiveHint == true)
        {
            return "destructive";
        }

        return annotations.IdempotentHint == true ? "idempotent input" : "input";
    }

    [Fact]
    public void EveryCoreToolIsListedUnderItsOwnHints()
    {
        using var host = new AutomationHost(new ProcessPolicy(["test-app"]), includeDesktopTools: true);
        var listed = Listed();

        var tools = host.Tools.GetToolDefinitions();
        Assert.Equal(tools.Count, listed.Count);
        foreach (var tool in tools)
        {
            var name = tool.Name["windows_".Length..];
            Assert.True(listed.ContainsKey(name), $"{name} is not in the README annotation table");
            Assert.True(
                Class(tool.Annotations!) == listed[name],
                $"{name}: README says {listed[name]}, code says {Class(tool.Annotations!)}"
            );
        }
    }
}
