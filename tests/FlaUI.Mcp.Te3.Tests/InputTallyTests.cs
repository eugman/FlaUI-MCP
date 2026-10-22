using FlaUI.Mcp;
using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>
/// The tally counts what Core counted (its outcome's inputsSent, or the inputs Core recorded during a direct send),
/// never what a tool's text claims. Scripted Core results and a fake desktop; nothing is sent.
/// </summary>
public sealed class InputTallyTests
{
    private static Func<Task<McpToolResult>> Returns(string text, ToolOutcome outcome, bool error = false) =>
        () =>
            Task.FromResult(
                new McpToolResult
                {
                    IsError = error,
                    Outcome = outcome,
                    Content = [new() { Text = text }],
                }
            );

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
}
