using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>What the run journal persists around a TE3 launch and a tool call; nothing is launched.</summary>
public sealed class RunJournalTests : IDisposable
{
    private readonly string journal = Path.Combine(
        Path.GetTempPath(),
        "fla_journal_test_" + Guid.NewGuid().ToString("N") + ".jsonl"
    );

    public void Dispose()
    {
        if (File.Exists(journal))
        {
            File.SetAttributes(journal, FileAttributes.Normal);
            File.Delete(journal);
        }
    }

    // The intent is written, then the journal turns read-only, so only the entry after the call fails.
    private void LockJournal() => File.SetAttributes(journal, FileAttributes.ReadOnly);

    [Fact]
    public void ALaunchThatStartsNothingFailsAfterSavingTheIntent()
    {
        var manifest = new RunManifest();
        var saves = 0;

        var error = Assert.Throws<InvalidOperationException>(() =>
            RunJournal.Launch(
                manifest,
                () => saves++,
                new System.Diagnostics.ProcessStartInfo("TabularEditor3.exe"),
                _ => null
            )
        );

        Assert.Contains("did not start", error.Message);
        Assert.Equal(1, saves);
        Assert.NotEqual(0, manifest.LaunchRequestedTicks);
        Assert.Equal(0, manifest.ProcessId);
    }

    [Fact]
    public async Task AResultTheJournalCannotRecordIsStillReturned()
    {
        var sent = new FlaUI.Mcp.McpToolResult
        {
            Content = [new() { Text = "Clicked w1e3. The click was sent; do not repeat it." }],
        };

        var result = await RunJournal.Tool(
            journal,
            "windows_click",
            new { @ref = "w1e3" },
            () =>
            {
                LockJournal();
                return Task.FromResult(sent);
            }
        );

        Assert.Equal(sent.Content[0].Text, result.Content[0].Text);
        Assert.Contains("could not be recorded", result.Content[^1].Text);
        Assert.Single(File.ReadAllLines(journal));
    }

    [Fact]
    public async Task AToolErrorTheJournalCannotRecordIsStillTheError()
    {
        var error = await Assert.ThrowsAsync<TimeoutException>(() =>
            RunJournal.Tool(
                journal,
                "windows_click",
                new { },
                () =>
                {
                    LockJournal();
                    return Task.FromException<FlaUI.Mcp.McpToolResult>(new TimeoutException("provider hung"));
                }
            )
        );

        Assert.Equal("provider hung", error.Message);
        Assert.Contains("could not be recorded", (string)error.Data[RunJournal.JournalErrorKey]!);
    }
}
