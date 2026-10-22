using System.Text.RegularExpressions;
using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>
/// Every path in the exe that sends input runs under a Te3Interaction grant that the user's own input revokes. Reads
/// the sources, or runs the runner up to its first refusal; nothing reaches the desktop.
/// </summary>
public sealed class RunGrantTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "fla_grant_test_" + Guid.NewGuid().ToString("N"));

    public RunGrantTests() => Directory.CreateDirectory(root);

    public void Dispose() => Directory.Delete(root, true);

    private static readonly Regex InstallsGrant = new(
        @"\.Guard\(\)|\bTe3McpServer\.Install\(|\bInstall\(host, interaction\)"
    );

    private static readonly Regex Member = new(@"^    (?:public|private|internal|protected)\b", RegexOptions.Multiline);

    /// <summary>
    /// Each <c>new AutomationHost(</c> whose own member (from the declaration before it to the next one) installs no
    /// grant, as "line: text". Per occurrence: a guarded host elsewhere in the file does not cover another one.
    /// </summary>
    internal static List<string> UnguardedHosts(string source)
    {
        var starts = Member.Matches(source).Select(m => m.Index).Append(source.Length).ToList();
        var unguarded = new List<string>();
        foreach (Match host in Regex.Matches(source, @"new AutomationHost\("))
        {
            var from = starts.LastOrDefault(i => i <= host.Index);
            var to = starts.First(i => i > host.Index);
            if (!InstallsGrant.IsMatch(source[from..to]))
            {
                var line = source[..host.Index].Count(c => c == '\n') + 1;
                unguarded.Add($"{line}: {source.Split('\n')[line - 1].Trim()}");
            }
        }

        return unguarded;
    }

    [Fact]
    public void EveryAutomationHostTheExeBuildsHasTheGrantInstalled()
    {
        var unguarded = Directory
            .GetFiles(DirectInputJournalTests.Sources(), "*.cs")
            .SelectMany(file =>
                UnguardedHosts(File.ReadAllText(file)).Select(hit => Path.GetFileName(file) + ":" + hit)
            )
            .ToList();

        Assert.Empty(unguarded);
    }

    [Fact]
    public void AGuardedHostDoesNotCoverAnotherHostInTheSameFile()
    {
        const string source = """
            public static class Commands
            {
                public static void Serve()
                {
                    using var host = new AutomationHost(policy);
                    using var guard = interaction.Guard();
                }

                public static void Replay()
                {
                    using var host = new AutomationHost(policy);
                }
            }
            """;

        var hits = UnguardedHosts(source);

        Assert.Single(hits);
        Assert.StartsWith("11:", hits[0]);
    }

    [Fact]
    public void NothingInTheExeStartsAProcessPastCoresGuardedStart()
    {
        // ArtifactFiles reads git's view of a file: no desktop change, so no lease or grant (see its comment).
        var raw = Directory
            .GetFiles(DirectInputJournalTests.Sources(), "*.cs")
            .Where(file => Path.GetFileName(file) != "ArtifactFiles.cs")
            .SelectMany(file =>
                File.ReadAllLines(file)
                    .Select((line, i) => (file, line, i))
                    .Where(x => Regex.IsMatch(x.line, @"\bProcess\.Start\b") && !x.line.TrimStart().StartsWith("//"))
            )
            .Select(x => $"{Path.GetFileName(x.file)}:{x.i + 1}: {x.line.Trim()}")
            .ToList();

        Assert.Empty(raw);
    }

    [Fact]
    public void TheRunCommandBeginsTheRunsGrantAndPassesItToEveryRecipe()
    {
        var program = File.ReadAllText(Path.Combine(DirectInputJournalTests.Sources(), "Program.cs"));

        Assert.Contains("new Te3Interaction()", program);
        Assert.Contains("RunGrant.Begin(", program);
        Assert.Matches(@"RunExecutor\.Execute\(\s*config,\s*recipe,\s*interaction,", program);
    }

    [Fact]
    public async Task ARunWithoutTheUsersApprovalStopsBeforeAnyWork()
    {
        using var interaction = new Te3Interaction(() => 0);

        var error = await Assert.ThrowsAsync<FlaUI.Mcp.Core.InputRefusedException>(() =>
            RunExecutor.Execute(
                new RunConfig { Output = root },
                new Recipe("test", "page", false, _ => Task.CompletedTask),
                interaction,
                CancellationToken.None,
                heldRunnerLock: null,
                new RunPaths(Path.Combine(root, "runner.lock"), Path.Combine(root, "settings"))
            )
        );

        Assert.Contains("approval", error.Message);
        Assert.Equal(Te3Interaction.ApprovalRequired, error.Refusal.Code);
        Assert.Empty(Directory.GetDirectories(root));
    }
}
