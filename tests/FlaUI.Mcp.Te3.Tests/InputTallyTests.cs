using FlaUI.Mcp;
using FlaUI.Mcp.Core;
using Xunit;
using static CoreResults;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>
/// The tally counts what Core counted (its outcome's inputsSent, or the inputs Core recorded during a direct send),
/// never what a tool's text claims. Scripted Core results and a fake desktop; nothing is sent.
/// </summary>
public sealed class InputTallyTests
{
    private static Func<Task<McpToolResult>> Returns(string text, ToolOutcome outcome, bool error = false) =>
        () => Task.FromResult(Result(outcome, error, text));

    private static ToolOutcome Outcome(string dispatch, int inputsSent, string provider = "not-tracked") =>
        new(dispatch, provider) { InputsSent = inputsSent, Status = dispatch == "completed" ? "ok" : "failed" };

    [Fact]
    public async Task ACompletedCallCountsEveryInputCoreCounted()
    {
        var inputs = new InputTally();

        await inputs.Invoke(Returns("Typed 2 lines", Outcome("completed", 3)), tool: "windows_type");

        Assert.Equal(3, inputs.Sent);
    }

    [Fact]
    public async Task AReadThatSentNothingCountsNothing()
    {
        var inputs = new InputTally();

        await inputs.Invoke(Returns("Found 1 element", Outcome("completed", 0)), tool: "windows_find");

        Assert.Equal(0, inputs.Sent);
    }

    [Fact]
    public async Task AFailureCountsWhatCoreCountedWhateverItsTextSays()
    {
        // Text that proves nothing either way: Core's count decides.
        var inputs = new InputTally();

        await Assert.ThrowsAsync<CoreCallException>(() =>
            inputs.Invoke(
                Returns("UIA call timed out", Outcome("not-dispatched", 0), error: true),
                tool: "windows_click"
            )
        );

        Assert.Equal(0, inputs.Sent);
    }

    [Fact]
    public async Task APartialCallCountsEachInputThatWentOut()
    {
        var inputs = new InputTally();

        await Assert.ThrowsAsync<CoreCallException>(() =>
            inputs.Invoke(
                Returns("Failed to send keys: Completed 2 chord(s).", Outcome("partial", 2), error: true),
                tool: "windows_send_keys"
            )
        );

        Assert.Equal(2, inputs.Sent);
    }

    [Fact]
    public async Task AFailedCallCarriesCoresOutcomeNotJustItsText()
    {
        var inputs = new InputTally();
        var outcome = Outcome("partial", 1) with
        {
            Failure = new InputRefusal("activation-denied", "Windows refused."),
        };

        var error = await Assert.ThrowsAsync<CoreCallException>(() =>
            inputs.Invoke(
                Returns("Failed to click w1e3: Windows refused.", outcome, error: true),
                tool: "windows_click"
            )
        );

        Assert.Equal("windows_click", error.Tool);
        Assert.Equal("partial", error.Outcome.Dispatch);
        Assert.Equal("activation-denied", error.Outcome.Failure?.Code);
        Assert.Equal("Failed to click w1e3: Windows refused.", error.Message);
    }

    [Theory]
    [InlineData("completed", "pending")]
    [InlineData("timed_out_pending", "unknown")]
    public async Task APendingCallIsAFailureThatSaysItIsPending(string dispatch, string provider)
    {
        var inputs = new InputTally();

        var error = await Assert.ThrowsAsync<CoreCallException>(() =>
            inputs.Invoke(Returns("Still running", Outcome(dispatch, 0, provider) with { Status = "ok" }))
        );

        Assert.True(error.Outcome.IsPending);
    }

    [Fact]
    public async Task ARefusalAfterTheTargetWasRestoredCounts()
    {
        // The refusal's text says no input was sent, but focusing the minimized target restored it first.
        var inputs = new InputTally();

        await Assert.ThrowsAsync<CoreCallException>(() =>
            inputs.Invoke(
                Returns(
                    "Failed to click w1e3: activation-denied: Windows kept 'WindowsTerminal' (PID 9) in the foreground. No input sent.",
                    Outcome("partial", 1),
                    error: true
                ),
                tool: "windows_click"
            )
        );

        Assert.Equal(1, inputs.Sent);
    }

    [Fact]
    public async Task APendingCallCountsItsDispatch()
    {
        var inputs = new InputTally();

        await Assert.ThrowsAsync<CoreCallException>(() =>
            inputs.Invoke(Returns("Invoke pending", Outcome("completed", 1, provider: "pending")))
        );

        Assert.Equal(1, inputs.Sent);
    }

    [Fact]
    public void ADirectSendCountsWhatCoreRecordedIncludingTheActivation()
    {
        // GuardedInput brought the target forward (one change) and then sent one input.
        var desktop = new FakeDesktop { ForegroundWindow = FakeDesktop.OtherHwnd };
        var inputs = new InputTally();
        using (desktop.Install())
        {
            inputs.Dispatch(() =>
            {
                using var input = new GuardedInput(FakeDesktop.Target);
                input.Send(() => { });
            });
        }

        Assert.Equal(2, inputs.Sent);
        Assert.Equal(1, desktop.Dispatched);
    }

    [Fact]
    public void ADirectSendRefusedBeforeAnythingChangedCountsNothing()
    {
        var desktop = new FakeDesktop { ForegroundWindow = FakeDesktop.OtherHwnd, ActivationAllowed = false };
        var inputs = new InputTally();
        using (desktop.Install())
        {
            Assert.Throws<InputRefusedException>(() =>
                inputs.Dispatch(() =>
                {
                    using var input = new GuardedInput(FakeDesktop.Target);
                    input.Send(() => { });
                })
            );
        }

        Assert.Equal(0, inputs.Sent);
        Assert.Equal(0, desktop.Dispatched);
    }

    [Fact]
    public async Task AnIntentTheJournalCannotWriteSendsNothing()
    {
        var inputs = new InputTally();
        var journal = Path.Combine(Path.GetTempPath(), "fla_missing_" + Guid.NewGuid().ToString("N"), "actions.jsonl");
        var called = false;

        await Assert.ThrowsAnyAsync<IOException>(() =>
            inputs.Invoke(() =>
                RunJournal.Tool(
                    journal,
                    "windows_click",
                    new { },
                    () =>
                    {
                        called = true;
                        return Returns("Clicked", Outcome("completed", 1))();
                    }
                )
            )
        );

        Assert.False(called);
        Assert.Equal(0, inputs.Sent);
    }

    [Fact]
    public async Task AnUnverifiedCallIsSentNotFailedAndIsRecordedAsUnproven()
    {
        // Core reports a mutating call with no proof as unverified: sent, not proven. It does not abort the recipe;
        // the tally keeps it so the run can say the step was not read back.
        var inputs = new InputTally();

        await inputs.Invoke(
            Returns("Clicked", new ToolOutcome("completed", "not-tracked") { InputsSent = 1, Status = "unverified" }),
            tool: "windows_click"
        );

        Assert.Equal(1, inputs.Sent);
        Assert.Equal([new UnverifiedStep("windows_click", null, null)], inputs.Unverified);
    }

    [Fact]
    public async Task AnUnverifiedCallIsRecordedWithTheRunsStepAndItsTarget()
    {
        var inputs = new InputTally { Step = "Run C# script" };

        await inputs.Invoke(
            Returns("Clicked", new ToolOutcome("completed", "not-tracked") { InputsSent = 1, Status = "unverified" }),
            tool: "windows_click",
            target: "w1e9"
        );
        await inputs.Invoke(
            Returns("Read", new ToolOutcome("completed", "not-tracked") { InputsSent = 0, Status = "ok" }),
            tool: "windows_find"
        );
        inputs.RecordUnverified("te3_page", "Save (Ctrl+S)", "w1");

        Assert.Equal(
            [
                new UnverifiedStep("windows_click", "Run C# script", "w1e9"),
                new UnverifiedStep("te3_page", "Save (Ctrl+S)", "w1"),
            ],
            inputs.Unverified
        );
    }

    [Fact]
    public void TheRunsManifestListsTheStepsThatWentOutWithoutAReadBack()
    {
        var inputs = new InputTally();
        inputs.RecordUnverified("windows_send_keys", "Save (Ctrl+S)", "w1");
        var manifest = new RunManifest();

        RunExecutor.RecordUnverified(manifest, inputs);
        var written = System.Text.Json.JsonSerializer.SerializeToElement(manifest, RunConfig.Json);

        Assert.Equal([new UnverifiedStep("windows_send_keys", "Save (Ctrl+S)", "w1")], manifest.UnverifiedSteps);
        Assert.Equal("Save (Ctrl+S)", written.GetProperty("unverifiedSteps")[0].GetProperty("step").GetString());
    }

    // Every key send of the page (any key, multi-line calls included) must be followed, in the same method, by a
    // read-back (Expect, Absent, Count, RequireSelectedTab) or by Unverified(...), or the manifest's unverifiedSteps
    // under-reports; Te3Page cannot run offline, so the source is the guard, with no count of sends assumed.
    [Fact]
    public void EveryKeySendInTheScriptsPageIsReadBackOrRecordedAsUnverified()
    {
        var lines = File.ReadAllLines(Path.Combine(DirectInputJournalTests.Sources(), "Te3Page.Scripts.cs"));
        var sends = Enumerable.Range(0, lines.Length).Where(i => IsKeySend(lines[i])).ToList();
        var unread = sends
            .Where(i =>
                !lines.Skip(i + 1).TakeWhile(line => line != "    }").Any(line => IsRead(line) || IsRecord(line))
            )
            .Select(i => $"line {i + 1}: {lines[i].Trim()}")
            .ToList();

        Assert.NotEmpty(sends);
        Assert.Empty(unread);

        // The ones recorded as unverified reach the manifest under their step names.
        var recorded = sends
            .Select(i => lines.Skip(i + 1).TakeWhile(line => line != "    }").FirstOrDefault(IsRecord))
            .OfType<string>()
            .Select(StepName)
            .ToList();
        var manifest = new RunManifest();
        var inputs = new InputTally();
        foreach (var step in recorded)
        {
            inputs.RecordUnverified("te3_page", step, "w1");
        }

        RunExecutor.RecordUnverified(manifest, inputs);
        Assert.NotEmpty(recorded);
        Assert.Equal(recorded, manifest.UnverifiedSteps.Select(step => step.Step));
    }

    private static bool IsKeySend(string line) => line.TrimStart().StartsWith("await Keys(", StringComparison.Ordinal);

    private static bool IsRead(string line)
    {
        var call = line.TrimStart();
        return call.StartsWith("await Expect(", StringComparison.Ordinal)
            || call.StartsWith("await Absent(", StringComparison.Ordinal)
            || call.StartsWith("await Count(", StringComparison.Ordinal)
            || call.StartsWith("await RequireSelectedTab(", StringComparison.Ordinal);
    }

    private static bool IsRecord(string line) => line.TrimStart().StartsWith("Unverified(\"", StringComparison.Ordinal);

    private static string StepName(string? line) => line!.Split('"')[1];
}
