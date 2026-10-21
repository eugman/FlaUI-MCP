using Xunit;

/// <summary>Every key the row strategy presses is checked against the walk's clock first; no desktop access.</summary>
public sealed class KeyBudgetTests
{
    [Fact]
    public async Task AKeyIsPressedWhileTimeIsLeft()
    {
        var presses = 0;

        await KeyBudget.Press(() => false, () => Task.FromResult(++presses), "Home");

        Assert.Equal(1, presses);
    }

    [Fact]
    public async Task NoKeyIsPressedOnceTheClockHasRunOut()
    {
        var presses = 0;

        var error = await Assert.ThrowsAsync<TimeoutException>(() =>
            KeyBudget.Press(() => true, () => Task.FromResult(++presses), "PageDown 2 of 3")
        );

        Assert.Equal(0, presses);
        Assert.Contains("PageDown 2 of 3", error.Message);
        Assert.Contains("no key sent", error.Message);
    }

    [Fact]
    public void TheRowStrategyChecksTheClockBeforeEachKeyItPresses()
    {
        var rows = File.ReadAllText(
            Path.Combine(FlaUI.Mcp.Te3.Tests.DirectInputJournalTests.Sources(), "Te3Page.Rows.cs")
        );

        // Every key in the row strategy goes through the budget, never straight to Keys.
        Assert.DoesNotMatch(@"(?<!KeyBudget\.Press\([^;]*)await (page\.)?Keys\(", rows);
    }
}
