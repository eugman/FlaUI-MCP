using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>Object navigation turns TOM Explorer's Show changes filter off before it walks the tree.</summary>
public sealed class ShowChangesGateTests
{
    private sealed class FakeCheckBox(bool? state, bool sticks = false, int staleReads = 0)
    {
        private bool? shown = state;

        public int Clicks { get; private set; }

        /// <summary>The state as UI Automation reports it; after a click it lags for <c>staleReads</c> reads.</summary>
        public Task<bool?> IsOn()
        {
            if (staleReads > 0 && Clicks > 0)
            {
                staleReads--;
                return Task.FromResult(shown);
            }

            shown = state;
            return Task.FromResult(state);
        }

        public Task Click()
        {
            Clicks++;
            if (!sticks && state is { } on)
            {
                state = !on;
            }

            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task AnActiveFilterIsTurnedOffWithOneClick()
    {
        var box = new FakeCheckBox(true);

        Assert.True(await ShowChangesGate.TurnOff(box.IsOn, box.Click));

        Assert.Equal(1, box.Clicks);
        Assert.False(await box.IsOn());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public async Task AnInactiveOrMissingFilterIsLeftAlone(bool? state)
    {
        var box = new FakeCheckBox(state);

        Assert.False(await ShowChangesGate.TurnOff(box.IsOn, box.Click));

        Assert.Equal(0, box.Clicks);
    }

    [Fact]
    public async Task AFilterThatReadsOnForAMomentAfterTheClickIsNotReported()
    {
        var box = new FakeCheckBox(true, staleReads: 2);

        Assert.True(await ShowChangesGate.TurnOff(box.IsOn, box.Click, waitMs: 50, pollMs: 1));

        Assert.Equal(1, box.Clicks);
    }

    [Fact]
    public async Task AFilterThatStaysOnIsReportedWithTheNextAction()
    {
        var box = new FakeCheckBox(true, sticks: true);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ShowChangesGate.TurnOff(box.IsOn, box.Click, waitMs: 5, pollMs: 1)
        );

        Assert.Contains("show-changes filter active", error.Message);
        Assert.Contains("Next:", error.Message);
        Assert.Equal(1, box.Clicks);
    }
}
