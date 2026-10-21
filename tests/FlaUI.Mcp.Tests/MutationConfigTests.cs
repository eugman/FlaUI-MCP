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
        "Tools/BatchTool.cs",
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
        var tool = new Regex(@"new (SendKeysTool|TypeTool|FillTool|ClickTool|MouseTool|PasteTool|WindowStateTool)\(");
        var test = new Regex(@"\[(Fact|Theory)\]");
        var project = Path.GetDirectoryName(
            RepoFile(Path.Combine("tests", "FlaUI.Mcp.Tests", "FlaUI.Mcp.Tests.csproj"))
        )!;
        var unguarded = new List<string>();
        foreach (var file in Directory.GetFiles(project, "*.cs"))
        {
            var text = File.ReadAllText(file);
            var starts = test.Matches(text).Select(m => m.Index).Append(text.Length).ToArray();
            for (var i = 0; i + 1 < starts.Length; i++)
            {
                var body = text[starts[i]..starts[i + 1]];
                if (tool.IsMatch(body) && !body.Contains(".Install()"))
                {
                    var name = Regex.Match(body, @"(?:void|Task)\s+(\w+)\(").Groups[1].Value;
                    unguarded.Add($"{Path.GetFileNameWithoutExtension(file)}.{name}");
                }
            }
        }

        Assert.Empty(unguarded);
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
}
