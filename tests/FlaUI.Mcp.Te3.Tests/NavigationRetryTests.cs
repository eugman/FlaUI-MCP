using FlaUI.Mcp;
using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>
/// A navigation is retried only when the whole attempt sent no input and Core's outcome for the failing call says it
/// was not dispatched. No text is read to decide.
/// </summary>
public sealed class NavigationRetryTests
{
    private int inputs;

    private Task Retry(Func<Task> attempt, List<string>? reports = null) =>
        NavigationRetry.Once(attempt, () => inputs, reports == null ? null : reports.Add);

    private static CoreCallException Core(
        string dispatch,
        string text = "Failed to click w1e3.",
        int inputsSent = 0,
        string provider = "not-tracked",
        string? code = null
    ) =>
        new(
            "windows_click",
            new McpToolResult
            {
                IsError = true,
                Content = [new() { Text = text }],
                Outcome = new ToolOutcome(dispatch, provider)
                {
                    Status = "failed",
                    InputsSent = inputsSent,
                    Failure = code == null ? null : new InputRefusal(code, text),
                },
            }
        );

    [Fact]
    public async Task AFailureThatIsNotACoreCallIsNotRetried()
    {
        var attempts = 0;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Retry(() =>
            {
                attempts++;
                throw new InvalidOperationException("Search text 'Date' was not found");
            })
        );

        Assert.Equal(1, attempts);
        Assert.StartsWith("Search text 'Date' was not found", error.Message);
    }

    [Fact]
    public async Task TextThatSaysNoInputWasSentIsNotEnough()
    {
        // The host's own lease wait, not a Core call: nothing structured says what went out.
        var attempts = 0;

        await Assert.ThrowsAsync<TimeoutException>(() =>
            Retry(() =>
            {
                attempts++;
                throw new TimeoutException("Desktop input is busy; no input sent.");
            })
        );

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task ACoreCallThatWasNotDispatchedIsRetriedOnce()
    {
        var attempts = 0;
        var reports = new List<string>();

        await Retry(
            () => attempts++ == 0 ? throw Core("not-dispatched", "Desktop input is busy.") : Task.CompletedTask,
            reports
        );

        Assert.Equal(2, attempts);
        Assert.Contains("Desktop input is busy", Assert.Single(reports));
    }

    [Theory]
    [InlineData("activation-denied")]
    [InlineData("desktop-unavailable")]
    public async Task ABlockedDesktopIsNotRetried(string code)
    {
        // The companion tells the caller that retrying will not help; the walk must not retry either.
        var attempts = 0;

        await Assert.ThrowsAsync<CoreCallException>(() =>
            Retry(() =>
            {
                attempts++;
                throw Core("not-dispatched", code: code);
            })
        );

        Assert.Equal(1, attempts);
    }

    [Theory]
    [InlineData("partial", "not-tracked")]
    [InlineData("completed", "pending")]
    [InlineData("timed_out_pending", "unknown")]
    public async Task ACallThatMayHaveGoneOutIsNotRetried(string dispatch, string provider)
    {
        // Core's outcome says input went out or may still go out, whatever the page's own tally shows.
        var attempts = 0;

        await Assert.ThrowsAsync<CoreCallException>(() =>
            Retry(() =>
            {
                attempts++;
                throw Core(dispatch, "Failed to send keys: no further input sent.", provider: provider);
            })
        );

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task AnAttemptThatSentInputBeforeItsRefusalIsNotRetried()
    {
        // The walk typed the search and pressed Down; only its last step was refused before input.
        var attempts = 0;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Retry(() =>
            {
                attempts++;
                inputs += 3;
                throw Core("not-dispatched", "Point is not on the target window (off-window or covered).");
            })
        );

        Assert.Equal(1, attempts);
        Assert.StartsWith("Point is not on the target window", error.Message);
        Assert.Contains("3 inputs", error.Message);
    }

    [Fact]
    public async Task WhenTheRetryAlsoFailsTheFirstErrorIsKept()
    {
        var attempts = 0;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Retry(() =>
                attempts++ == 0
                    ? throw Core("not-dispatched", "Clipboard is busy.")
                    : throw new InvalidOperationException("Object identity unverified")
            )
        );

        Assert.StartsWith("Clipboard is busy.", error.Message);
        Assert.Contains("Object identity unverified", error.Message);
        Assert.Equal("Clipboard is busy.", error.InnerException?.Message);
    }
}
