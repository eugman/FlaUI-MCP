using System.Text.Json;
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
        "Tools/ClickTool.cs",
        "Tools/MouseTool.cs",
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
}
