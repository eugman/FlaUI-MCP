using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>A navigation is retried only when the whole attempt sent no input and was refused before any.</summary>
public sealed class NavigationRetryTests
{
    private int inputs;

    private Task Retry(Func<Task> attempt, List<string>? reports = null) =>
        NavigationRetry.Once(attempt, () => inputs, reports == null ? null : reports.Add);

    [Fact]
    public async Task AFailureThatMayHaveSentInputIsNotRetried()
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
    public async Task ARefusalBeforeAnyInputIsRetriedOnce()
    {
        var attempts = 0;
        var reports = new List<string>();

        await Retry(
            () =>
                attempts++ == 0
                    ? throw new TimeoutException("Desktop input is busy; no input sent.")
                    : Task.CompletedTask,
            reports
        );

        Assert.Equal(2, attempts);
        Assert.Contains("Desktop input is busy", Assert.Single(reports));
    }

    [Theory]
    [InlineData(
        "Failed to type w1e2: activation-denied: Windows kept 'WindowsTerminal' (PID 9) in the foreground instead of target PID 7. "
    )]
    [InlineData(
        "Failed to type w1e2: desktop-unavailable: Windows reports no foreground window; the session may be locked."
    )]
    public async Task ABlockedDesktopIsNotRetried(string refusal)
    {
        // The companion tells the caller that retrying will not help; the walk must not retry either.
        var attempts = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Retry(() =>
            {
                attempts++;
                throw new InvalidOperationException(refusal);
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
                throw new InvalidOperationException(
                    "Point is not on the target window (off-window or covered); no input sent."
                );
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
                throw new InvalidOperationException(
                    attempts++ == 0 ? "Clipboard is busy; no input sent." : "Object identity unverified"
                )
            )
        );

        Assert.StartsWith("Clipboard is busy; no input sent.", error.Message);
        Assert.Contains("Object identity unverified", error.Message);
        Assert.Equal("Clipboard is busy; no input sent.", error.InnerException?.Message);
    }
}
