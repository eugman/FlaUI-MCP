using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

public sealed class LocalContentGateTests
{
    [Fact]
    public void IgnoredReplicaInputsAreIncludedOnlyWhenExplicitlyEnabled()
    {
        var root = Path.Combine(Path.GetTempPath(), "te3-content-gate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "replica-inputs"));
        try
        {
            File.WriteAllText(Path.Combine(root, "replica-inputs", "probe.txt"), "input");
            var project = FindProject();

            Assert.Empty(LocalLinks(project, root, false));
            Assert.Equal(["replica-inputs/probe.txt"], LocalLinks(project, root, true));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string FindProject()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var project = Path.Combine(dir.FullName, "src", "FlaUI.Mcp.Te3", "FlaUI.Mcp.Te3.csproj");
            if (File.Exists(project))
            {
                return project;
            }
        }

        throw new FileNotFoundException("TE3 project was not found above the test output.");
    }

    private static string[] LocalLinks(string project, string root, bool enabled)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("msbuild");
        start.ArgumentList.Add(project);
        start.ArgumentList.Add("-getItem:Content");
        start.ArgumentList.Add("-nologo");
        start.ArgumentList.Add("-p:IncludeLocalRecipes=" + enabled.ToString().ToLowerInvariant());
        start.ArgumentList.Add("-p:LocalRoot=" + root + Path.DirectorySeparatorChar);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("dotnet msbuild did not start");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(30_000), "dotnet msbuild timed out");
        Assert.True(process.ExitCode == 0, stderr);
        using var result = JsonDocument.Parse(stdout);
        return result
            .RootElement.GetProperty("Items")
            .GetProperty("Content")
            .EnumerateArray()
            .Where(item =>
                item.TryGetProperty("Link", out var link) && link.GetString()?.StartsWith("replica-inputs/") == true
            )
            .Select(item => item.GetProperty("Link").GetString()!)
            .ToArray();
    }
}
