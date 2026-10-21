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

    [Fact]
    public async Task ADirectInputIsJournaledBeforeAndAfterItIsSent()
    {
        string[]? seen = null;

        await RunJournal.Input(
            journal,
            "te3_pointer_move",
            new { x = 10, y = 20 },
            () => seen = File.ReadAllLines(journal)
        );

        var intent = System.Text.Json.JsonDocument.Parse(Assert.Single(seen!)).RootElement;
        Assert.Equal("intent", intent.GetProperty("phase").GetString());
        Assert.Equal("te3_pointer_move", intent.GetProperty("tool").GetString());
        Assert.Equal(10, intent.GetProperty("arguments").GetProperty("x").GetInt32());
        var after = System.Text.Json.JsonDocument.Parse(File.ReadAllLines(journal)[^1]).RootElement;
        Assert.Equal("result", after.GetProperty("phase").GetString());
    }

    [Fact]
    public async Task AFailedDirectInputIsJournaledAsAnError()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RunJournal.Input(journal, "te3_click", new { }, () => throw new InvalidOperationException("obscured"))
        );

        var after = System.Text.Json.JsonDocument.Parse(File.ReadAllLines(journal)[^1]).RootElement;
        Assert.Equal("error", after.GetProperty("phase").GetString());
        Assert.Equal("obscured", after.GetProperty("error").GetString());
    }

    [Fact]
    public async Task WithoutAJournalTheInputIsJustSent()
    {
        var sent = false;

        await RunJournal.Input(null, "te3_click", new { }, () => sent = true);

        Assert.True(sent);
        Assert.False(File.Exists(journal));
    }

    [Fact]
    public async Task TheManifestSaysWhenTheFailingCallsOutcomeIsMissingFromTheJournal()
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
        var manifest = new RunManifest();

        RunExecutor.RecordFailure(manifest, error);

        Assert.Equal("provider hung", manifest.Error);
        Assert.Contains("could not be recorded", manifest.JournalError);
    }

    [Fact]
    public async Task TheJournalRecordsCoresOutcomeForEachCall()
    {
        // McpToolResult.Outcome is not serialized; the journal writes the outcome itself, as structuredContent.
        await RunJournal.Tool(
            journal,
            "windows_click",
            new { @ref = "w1e3" },
            () =>
                Task.FromResult(
                    new FlaUI.Mcp.McpToolResult
                    {
                        IsError = true,
                        Content = [new() { Text = "Failed to click w1e3." }],
                        Outcome = new FlaUI.Mcp.Core.ToolOutcome("partial", "not-tracked")
                        {
                            Status = "failed",
                            InputsSent = 2,
                            Failure = new FlaUI.Mcp.Core.InputRefusal("activation-denied", "Windows refused."),
                        },
                    }
                )
        );

        var entry = System.Text.Json.JsonDocument.Parse(File.ReadLines(journal).Last()).RootElement;
        Assert.Equal("result", entry.GetProperty("phase").GetString());
        Assert.True(entry.GetProperty("isError").GetBoolean());
        Assert.Equal("Failed to click w1e3.", entry.GetProperty("content")[0].GetProperty("text").GetString());
        var outcome = entry.GetProperty("structuredContent");
        Assert.Equal("partial", outcome.GetProperty("dispatch").GetString());
        Assert.Equal(2, outcome.GetProperty("inputsSent").GetInt32());
        Assert.Equal("activation-denied", outcome.GetProperty("failure").GetProperty("code").GetString());
    }

    [Fact]
    public void AnActivationTimeoutDoesNotClaimNothingWasSent()
    {
        // te3_focus_window was sent before the wait, so the timeout must not read as a call that was not dispatched.
        Assert.False(CoreFailure.NotDispatched(new TimeoutException(RunExecutor.ActivationTimeout)));
        Assert.Contains("focus", RunExecutor.ActivationTimeout);
    }

    [Fact]
    public void AJournalErrorUnderAWrappingFailureStillReachesTheManifest()
    {
        // NavigationRetry and DialogCancel wrap the call's own exception.
        var call = new TimeoutException("provider hung");
        call.Data[RunJournal.JournalErrorKey] = "Journal: this call's outcome could not be recorded.";
        var manifest = new RunManifest();

        RunExecutor.RecordFailure(manifest, new InvalidOperationException("provider hung The retry also failed", call));

        Assert.Contains("could not be recorded", manifest.JournalError);
    }
}
