using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// Mutation testing runs every test once per mutant. It must only ever run this offline project: the
/// integration tests launch apps and take the desktop from whoever is using the machine.
/// </summary>
public sealed class MutationConfigTests
{
    private static readonly string[] GuardFiles =
    [
        "Core/GuardedInput.cs",
        "Core/GuardedDesktop.cs",
        "Core/ProcessIntegrity.cs",
        "Core/WindowMutation.cs",
        "Core/ClickPoint.cs",
        "Core/InputRefusal.cs",
        "Core/ElementRegistry.cs",
        "Core/PendingInvokeTracker.cs",
        "Core/OperationContext.cs",
        "Mcp/McpServer.cs",
        "Mcp/McpLineTransport.cs",
        "Mcp/ToolRegistry.cs",
        "Mcp/ToolBase.cs",
        "Tools/ClickTool.cs",
        "Tools/ClickToolReplies.cs",
        "Tools/MouseTool.cs",
        "Tools/PointerActions.cs",
        "Tools/SendKeysTool.cs",
        "Tools/TypeTools.cs",
        "Tools/FillToolReplies.cs",
        "Tools/BatchTool.cs",
        "Core/WindowBaseline.cs",
        "Core/WaitOutcome.cs",
        "Core/Diagnostics.cs",
    ];

    private static string RepoFile(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(relative);
    }

    private static JsonElement Config() =>
        JsonDocument.Parse(File.ReadAllText(RepoFile("stryker-config.json"))).RootElement.GetProperty("stryker-config");

    [Fact]
    public void TheConfigNamesOnlyTheOfflineTestProject()
    {
        var config = Config();

        var projects = config.GetProperty("test-projects").EnumerateArray().Select(p => p.GetString()).ToArray();

        Assert.Equal(["FlaUI.Mcp.Tests.csproj"], projects);
        Assert.False(config.TryGetProperty("solution", out _));
        Assert.DoesNotContain("IntegrationTests", File.ReadAllText(RepoFile("stryker-config.json")));
    }

    [Fact]
    public void TheConfigMutatesExactlyTheGuardAndProtocolFiles()
    {
        var mutate = Config().GetProperty("mutate").EnumerateArray().Select(p => p.GetString()).ToArray();

        Assert.Equal(GuardFiles.Select(file => $"**/{file}").Order(), mutate.Order());
    }

    [Fact]
    public void EveryMutatedFileBelongsToAPartOfTheScript()
    {
        var script = File.ReadAllText(RepoFile(Path.Combine("scripts", "mutation.ps1")));
        var parts = script[script.IndexOf("$parts = [ordered]@{", StringComparison.Ordinal)..];
        parts = parts[..parts.IndexOf('}')];

        Assert.All(GuardFiles, file => Assert.Contains($"'{Path.GetFileNameWithoutExtension(file)}'", parts));
    }

    [Fact]
    public void TheScriptRunsStrykerInsideTheOfflineProjectAndGuardsTheRun()
    {
        var script = File.ReadAllText(RepoFile(Path.Combine("scripts", "mutation.ps1")));

        Assert.Contains("Push-Location (Join-Path $work 'tests/FlaUI.Mcp.Tests')", script);
        Assert.Contains("'--test-project', $offlineProject", script);
        Assert.Contains("$offlineProject = 'FlaUI.Mcp.Tests.csproj'", script);
        Assert.Contains("IsSolutionContext: True", script);
        Assert.Contains("throw \"Mutation testing ($name) aborted before running tests", script);
    }

    [Fact]
    public void TheOfflineAssemblyTurnsOffTheRealDesktop()
    {
        Assert.Equal("1", Environment.GetEnvironmentVariable("FLAUI_OFFLINE_TESTS"));
        Assert.Contains(
            "$env:FLAUI_OFFLINE_TESTS = '1'",
            File.ReadAllText(RepoFile(Path.Combine("scripts", "mutation.ps1")))
        );
    }

    [Fact]
    public void EveryTestThatRunsAnInputToolInstallsTheFakeDesktop()
    {
        var tool = new Regex(
            @"new (SendKeysTool|TypeTool|FillTool|ClickTool|MouseTool|PasteTool|WindowStateTool|FocusWindowTool|CloseWindowTool|LaunchTool|WindowPlacementTool|BatchTool|ScreenshotTool)\("
        );
        var member = new Regex(
            @"^    (?:public|private|internal|protected)\b[^\n=;]*?\b(\w+)\s*(?:<[^>]*>)?\(",
            RegexOptions.Multiline
        );
        var project = Path.GetDirectoryName(
            RepoFile(Path.Combine("tests", "FlaUI.Mcp.Tests", "FlaUI.Mcp.Tests.csproj"))
        )!;
        var unguarded = new List<string>();
        foreach (var file in Directory.GetFiles(project, "*.cs"))
        {
            var members = Members(File.ReadAllText(file), member);
            // Helpers that build an input tool count as building it in every test that calls them.
            var helpers = members.Where(m => !m.IsTest && tool.IsMatch(m.Body)).Select(m => m.Name).ToArray();
            unguarded.AddRange(
                members
                    .Where(m => m.IsTest && !m.Body.Contains(".Install()"))
                    .Where(m => tool.IsMatch(m.Body) || helpers.Any(h => Regex.IsMatch(m.Body, $@"\b{h}\(")))
                    .Select(m => $"{Path.GetFileNameWithoutExtension(file)}.{m.Name}")
            );
        }

        Assert.Empty(unguarded);
    }

    /// <summary>Each member of a test file, whether it is a test, and its text up to the next member.</summary>
    private static List<(string Name, bool IsTest, string Body)> Members(string text, Regex member)
    {
        var starts = member.Matches(text).ToArray();
        var members = new List<(string, bool, string)>();
        for (var i = 0; i < starts.Length; i++)
        {
            var start = starts[i].Index;
            var end = i + 1 < starts.Length ? starts[i + 1].Index : text.Length;
            var before = text[(i > 0 ? starts[i - 1].Index : 0)..start];
            var lastClose = before.LastIndexOf("\n    }", StringComparison.Ordinal);
            var attributes = lastClose >= 0 ? before[lastClose..] : before;
            var isTest = attributes.Contains("[Fact") || attributes.Contains("[Theory");
            members.Add((starts[i].Groups[1].Value, isTest, text[start..end]));
        }

        return members;
    }

    [Fact]
    public void TheCoreOwnershipCheckFailsWithoutAMergeBase()
    {
        // With no merge-base, `git diff $base HEAD` compares nothing useful and would pass any change.
        var script = File.ReadAllText(RepoFile(Path.Combine("scripts", "check-core-untouched.ps1")));
        var mergeBase = script.IndexOf("$base = git merge-base HEAD core/main", StringComparison.Ordinal);

        Assert.True(mergeBase >= 0);
        var next = script[mergeBase..];
        Assert.Matches(
            @"^\$base = git merge-base HEAD core/main\r?\n\s+if \(\$LASTEXITCODE -ne 0 -or -not \$base\)",
            next
        );
        Assert.Contains("has no common history with core/main", next);
    }

    [Fact]
    public void TheMutationScriptRestoresTheEnvironmentItChanged()
    {
        var script = File.ReadAllText(RepoFile(Path.Combine("scripts", "mutation.ps1")));
        var finallyAt = script.LastIndexOf("finally", StringComparison.Ordinal);

        Assert.True(finallyAt >= 0);
        var restore = script[finallyAt..];
        Assert.Contains("$env:FLAUI_OFFLINE_TESTS = $saved.FLAUI_OFFLINE_TESTS", restore);
        Assert.Contains("$env:UseSharedCompilation = $saved.UseSharedCompilation", restore);
        Assert.Contains("$env:MSBUILDDISABLENODEREUSE = $saved.MSBUILDDISABLENODEREUSE", restore);
    }

    [Fact]
    public void ThePointerIntegrationConfigIsTheOnlyOneThatNamesTheIntegrationTests()
    {
        var path = RepoFile("stryker-config.pointer-integration.json");
        var config = JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("stryker-config");
        var script = File.ReadAllText(RepoFile(Path.Combine("scripts", "mutation.ps1")));

        Assert.Equal(
            ["FlaUI.Mcp.IntegrationTests.csproj"],
            config.GetProperty("test-projects").EnumerateArray().Select(p => p.GetString()).ToArray()
        );
        Assert.Equal(1, config.GetProperty("concurrency").GetInt32());
        Assert.Equal(180000, config.GetProperty("additional-timeout").GetInt32());
        Assert.Contains("if ($Part -eq 'pointer-integration')", script);
        Assert.Contains("'stryker-config.pointer-integration.json' 180", script);
        Assert.Contains("if ($project -ne $offlineProject) { CloseTestApps }", script);
    }
}
