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
    [InlineData("Input target/focus changed or is disabled; no further input sent.")]
    public async Task ARefusedFirstClickSendsNothing(string refusal)
    {
        var inputs = new InputTally();

        await Assert.ThrowsAsync<InvalidOperationException>(() => inputs.Invoke(Returns(refusal, error: true)));

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
    public void ADirectInputCountsOnlyOnceItWasSent()
    {
        var inputs = new InputTally();

        Assert.Throws<InvalidOperationException>(() =>
            inputs.Send(() => throw new InvalidOperationException("Default layout click point is obscured"))
        );
        inputs.Send(() => { });

        Assert.Equal(1, inputs.Sent);
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
}
