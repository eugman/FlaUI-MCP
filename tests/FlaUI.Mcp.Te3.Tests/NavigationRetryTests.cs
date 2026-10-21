using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>A navigation is retried only when its failure proves no input went out.</summary>
public sealed class NavigationRetryTests
{
    [Fact]
    public async Task AFailureThatMayHaveSentInputIsNotRetried()
    {
        var attempts = 0;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NavigationRetry.Once(
                () =>
                {
                    attempts++;
                    throw new InvalidOperationException("Search text 'Date' was not found");
                },
                null
            )
        );

        Assert.Equal(1, attempts);
        Assert.Equal("Search text 'Date' was not found", error.Message);
    }

    [Theory]
    [InlineData("Desktop input is busy; no input sent.")]
    [InlineData("Windows refused to activate TE3. The blocked step sent no input.")]
    public async Task ARefusalBeforeAnyInputIsRetriedOnce(string refusal)
    {
        var attempts = 0;
        var reports = new List<string>();

        await NavigationRetry.Once(
            () => attempts++ == 0 ? throw new InvalidOperationException(refusal) : Task.CompletedTask,
            reports.Add
        );

        Assert.Equal(2, attempts);
        Assert.Contains(refusal, Assert.Single(reports));
    }

    [Fact]
    public async Task WhenTheRetryAlsoFailsTheFirstErrorIsKept()
    {
        var attempts = 0;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NavigationRetry.Once(
                () =>
                    throw new InvalidOperationException(
                        attempts++ == 0 ? "Clipboard is busy; no input sent." : "Object identity unverified"
                    ),
                null
            )
        );

        Assert.StartsWith("Clipboard is busy; no input sent.", error.Message);
        Assert.Contains("Object identity unverified", error.Message);
        Assert.Equal("Clipboard is busy; no input sent.", error.InnerException?.Message);
    }
}
