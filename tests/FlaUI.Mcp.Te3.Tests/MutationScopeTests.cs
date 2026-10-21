using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>Mutation testing runs only the offline TE3 tests, never a desktop test project. Reads files; runs pwsh.</summary>
public sealed class MutationScopeTests
{
    private const string TestProject = "FlaUI.Mcp.Te3.Tests.csproj";

    private static string RepositoryFile(string relative)
    {
        for (
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            directory != null;
            directory = directory.Parent
        )
        {
            var path = Path.Combine(directory.FullName, relative);
            if (File.Exists(path))
            {
                return path;
            }
        }

        throw new FileNotFoundException(relative + " not found above " + AppContext.BaseDirectory);
    }

    [Fact]
    public void TheStrykerConfigNamesOnlyTheOfflineTestProject()
    {
        var text = File.ReadAllText(
            RepositoryFile(Path.Combine("tests", "FlaUI.Mcp.Te3.Tests", "stryker-config.json"))
        );
        var config = JsonDocument.Parse(text).RootElement.GetProperty("stryker-config");

        Assert.Equal("FlaUI.Mcp.Te3.csproj", config.GetProperty("project").GetString());
        Assert.Equal([TestProject], config.GetProperty("test-projects").EnumerateArray().Select(p => p.GetString()));
        Assert.False(config.TryGetProperty("solution", out _));
        Assert.DoesNotContain(".sln", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IntegrationTests", text, StringComparison.OrdinalIgnoreCase);
    }

    private static int Guard(string line, int expectedTests)
    {
        using var guard = Process.Start(
            new ProcessStartInfo("pwsh")
            {
                ArgumentList =
                {
                    "-NoProfile",
                    "-File",
                    RepositoryFile(Path.Combine("scripts", "stryker-guard.ps1")),
                    "-Line",
                    line,
                    "-ExpectedTests",
                    expectedTests.ToString(System.Globalization.CultureInfo.InvariantCulture),
                },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        )!;
        guard.StandardOutput.ReadToEnd();
        guard.StandardError.ReadToEnd();
        guard.WaitForExit();
        return guard.ExitCode;
    }

    [Theory]
    [InlineData("[20:08:49 INF] Analyzing 1 test project(s).", 0)]
    [InlineData("[20:08:49 INF] Analyzing 2 test project(s).", 1)]
    [InlineData(
        @"[20:09:01 INF] Building test project C:\r\tests\FlaUI.Mcp.Te3.Tests\FlaUI.Mcp.Te3.Tests.csproj (1/1)",
        0
    )]
    [InlineData(
        @"[20:09:01 INF] Building test project C:\r\tests\FlaUI.Mcp.IntegrationTests\FlaUI.Mcp.IntegrationTests.csproj (1/1)",
        1
    )]
    [InlineData(@"[20:09:01 INF] Building test project C:\r\tests\FlaUI.Mcp.Tests\FlaUI.Mcp.Tests.csproj (1/2)", 1)]
    [InlineData(@"[20:09:20 INF] Number of tests found: 431 for project C:\src\FlaUI.Mcp.Te3\FlaUI.Mcp.Te3.csproj.", 0)]
    [InlineData(@"[20:09:20 INF] Number of tests found: 900 for project C:\src\FlaUI.Mcp.Te3\FlaUI.Mcp.Te3.csproj.", 1)]
    [InlineData("[20:10:00 WRN] An unidentified mutation resulted in a compile error", 0)]
    public void TheMutationRunStopsBeforeTestingAnythingButTheOfflineProject(string line, int exitCode) =>
        Assert.Equal(exitCode, Guard(line, expectedTests: 431));
}
