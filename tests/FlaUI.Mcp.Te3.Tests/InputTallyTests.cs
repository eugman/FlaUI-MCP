using FlaUI.Mcp;
using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>An input counts once it went out, not when it was attempted; scripted Core results, no desktop.</summary>
public sealed class InputTallyTests
{
    private const string ActivationDenied =
        "Failed to click w1e3: activation-denied: Windows kept 'WindowsTerminal' (PID 9) in the foreground instead of target PID 7. ";

    private static Func<Task<McpToolResult>> Returns(string text, bool error = false, ToolOutcome? outcome = null) =>
        () =>
            Task.FromResult(
                new McpToolResult
                {
                    IsError = error,
                    Outcome = outcome,
                    Content = [new() { Text = text }],
                }
            );

    [Theory]
    [InlineData(ActivationDenied)]
    [InlineData(
        "Failed to click w1e3: desktop-unavailable: Windows reports no foreground window; the session may be locked."
    )]
    [InlineData("Point is not on the target window (off-window or covered); no input sent.")]
    [InlineData("Desktop input needs the user's approval: keyboard or mouse input from the user was observed.")]
    [InlineData("Failed to click w1e3: Stale element reference.")]
    public async Task ARefusedFirstClickSendsNothing(string refusal)
    {
        var inputs = new InputTally();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            inputs.Invoke(Returns(refusal, error: true), tool: "windows_click")
        );

        Assert.Equal(0, inputs.Sent);
        Assert.False(
            System
                .Text.Json.JsonDocument.Parse(
                    Te3Companion.Failure("navigate", "click", refusal, inputs.Sent).Content[0].Text!
                )
                .RootElement.GetProperty("actionDispatched")
                .GetBoolean()
        );
    }

    [Fact]
    public async Task OneClickThenARefusalIsOneInput()
    {
        var inputs = new InputTally();

        await inputs.Invoke(Returns("Clicked w1e3."));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            inputs.Invoke(Returns(ActivationDenied, error: true))
        );

        Assert.Equal(1, inputs.Sent);
        var reply = System
            .Text.Json.JsonDocument.Parse(
                Te3Companion.Failure("navigate", "click", ActivationDenied, inputs.Sent).Content[0].Text!
            )
            .RootElement;
        Assert.True(reply.GetProperty("actionDispatched").GetBoolean());
        Assert.Equal(1, reply.GetProperty("inputsSent").GetInt32());
    }

    [Fact]
    public async Task AnErrorThatDoesNotProveARefusalCountsAsSent()
    {
        var inputs = new InputTally();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            inputs.Invoke(Returns("UIA call timed out", error: true))
        );

        Assert.Equal(1, inputs.Sent);
    }

    [Fact]
    public async Task APendingCallWentOut()
    {
        var inputs = new InputTally();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            inputs.Invoke(Returns("Invoke pending", outcome: new ToolOutcome("completed", "pending")))
        );

        Assert.Equal(1, inputs.Sent);
    }

    [Fact]
    public async Task AReadIsNotInput()
    {
        var inputs = new InputTally();

        await inputs.Invoke(Returns("Saved screenshot"), readOnly: true);

        Assert.Equal(0, inputs.Sent);
    }

    [Fact]
    public void ADirectInputThatFailsAfterItsChecksPassedCounts()
    {
        // GuardedInput's last check passed, so the click may have pressed the button before it threw.
        var inputs = new InputTally();

        Assert.Throws<InvalidOperationException>(() =>
            inputs.Dispatch(() => throw new InvalidOperationException("Mouse up failed after mouse down"))
        );
        inputs.Dispatch(() => { });

        Assert.Equal(2, inputs.Sent);
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
                        return Returns("Clicked")();
                    }
                )
            )
        );

        Assert.False(called);
        Assert.Equal(0, inputs.Sent);
    }

    // Core's own wording when a multi-part tool stops part-way (SendKeysTool, TypeTools).
    private const string SecondChordRefused =
        "Failed to send keys: Input target/focus changed or is disabled; no further input sent. Completed 1 chord(s). "
        + "A failing chord may have partially executed; inspect state before replaying input.";

    [Theory]
    [InlineData("windows_send_keys", SecondChordRefused)]
    [InlineData(
        "windows_send_keys",
        "Failed to send keys: Stale element reference. Completed 2 chord(s). A failing chord may have partially executed; inspect state before replaying input."
    )]
    [InlineData(
        "windows_type",
        "Failed to type: Desktop input needs the user's approval: keyboard or mouse input from the user was observed; ask them before resuming. Ask the user, then call te3_interaction_begin. Typed 0 of 1 line(s); line 1 may be partly typed."
    )]
    [InlineData(
        "windows_type",
        "Failed to type: Input target/focus changed or is disabled; no further input sent. Typed 1 of 2 line(s); line 2 may be partly typed."
    )]
    [InlineData(
        "windows_fill",
        "Failed to fill w1e2: Desktop input needs the user's approval: keyboard or mouse input from the user was observed."
    )]
    [InlineData(
        "windows_click",
        "Failed to click w1e3: Input target/focus changed or is disabled; no further input sent."
    )]
    public async Task APartlySentCallCountsAsSent(string tool, string failure)
    {
        var inputs = new InputTally();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            inputs.Invoke(Returns(failure, error: true), tool: tool)
        );

        Assert.Equal(1, inputs.Sent);
    }

    [Fact]
    public async Task KeysRefusedBeforeTheFirstChordSendNothing()
    {
        var inputs = new InputTally();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            inputs.Invoke(
                Returns(
                    "Failed to send keys: Desktop input is busy; no input sent. Completed 0 chord(s). No keyboard input was dispatched.",
                    error: true
                ),
                tool: "windows_send_keys"
            )
        );

        Assert.Equal(0, inputs.Sent);
    }

    [Fact]
    public async Task ASearchClearThatLostFocusBetweenItsKeysIsNotRetried()
    {
        // ClearTomSearchTyped sends Ctrl+A then Backspace in one call; focus moved between them.
        var inputs = new InputTally();
        var attempts = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NavigationRetry.Once(
                () =>
                {
                    attempts++;
                    return inputs.Invoke(Returns(SecondChordRefused, error: true), tool: "windows_send_keys");
                },
                () => inputs.Sent,
                null
            )
        );

        Assert.Equal(1, attempts);
    }
}
