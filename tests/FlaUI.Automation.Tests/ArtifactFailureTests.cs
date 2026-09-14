using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace FlaUI.Automation.Tests;

public sealed class ArtifactFailureTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "fla_artifact_failure_" + Guid.NewGuid().ToString("N"));
    public ArtifactFailureTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);

    [Fact]
    public async Task TimedOutGitCheckTerminatesItsOwnedProcess()
    {
        var start = Start("powershell.exe", "-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 30");

        var error = await Assert.ThrowsAsync<TimeoutException>(() => ArtifactFiles.RunGitCheck(start, TimeSpan.FromMilliseconds(200)));

        Assert.Contains("was terminated", error.Message);
        Assert.Contains("Destination was not overwritten", error.Message);
        var pid = int.Parse(Regex.Match(error.Message, @"PID (\d+)").Groups[1].Value);
        Assert.Throws<ArgumentException>(() => Process.GetProcessById(pid));
    }

    [Fact]
    public async Task GitCheckRetainsExitCodeForTrackedCleanDecision()
        => Assert.Equal(1, await ArtifactFiles.RunGitCheck(Start("cmd.exe", "/d", "/c", "exit 1")));

    [Fact]
    public async Task TimedOutGitCheckAlsoTerminatesItsOwnedChild()
    {
        var childFile = Path.Combine(root, "child.pid");
        var command = "$child = Start-Process powershell.exe -ArgumentList '-NoProfile -NonInteractive -Command Start-Sleep -Seconds 30' -WindowStyle Hidden -PassThru; " +
            "[System.IO.File]::WriteAllText('" + childFile.Replace("'", "''") + "', [string]$child.Id); $child.WaitForExit()";

        var error = await Assert.ThrowsAsync<TimeoutException>(() => ArtifactFiles.RunGitCheck(
            Start("powershell.exe", "-NoProfile", "-NonInteractive", "-Command", command), TimeSpan.FromSeconds(3)));

        Assert.Contains("was terminated", error.Message);
        Assert.True(File.Exists(childFile), "The fake Git process must start its child before the deadline.");
        var childPid = int.Parse(File.ReadAllText(childFile));
        Assert.Throws<ArgumentException>(() => Process.GetProcessById(childPid));
    }

    private static ProcessStartInfo Start(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return start;
    }

    [Fact]
    public void JournalReportsWriteAndCleanupFailuresWithoutReplacingExistingContent()
    {
        var path = Path.Combine(root, "journal.json");
        File.WriteAllText(path, "original");
        var cleanupError = new IOException("cleanup unavailable");

        var error = Assert.Throws<AggregateException>(() => AtomicJournal.Write(path, new FailingValue(), new JsonSerializerOptions(),
            _ => throw cleanupError));

        Assert.Contains("serialization failed", error.InnerExceptions[0].ToString());
        Assert.Same(cleanupError, error.InnerExceptions[1]);
        Assert.Equal("original", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(root, "*.tmp"));
    }

    [Fact]
    public void JournalPreservesOriginalFailureWhenCleanupSucceeds()
    {
        var error = Assert.Throws<InvalidOperationException>(() => AtomicJournal.Write(Path.Combine(root, "journal.json"),
            new FailingValue(), new JsonSerializerOptions()));

        Assert.Equal("serialization failed", error.Message);
        Assert.Empty(Directory.GetFiles(root));
    }

    private sealed class FailingValue
    {
        public string Value => throw new InvalidOperationException("serialization failed");
    }
}
