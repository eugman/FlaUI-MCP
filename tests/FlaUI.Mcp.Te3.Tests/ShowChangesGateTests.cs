using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>Object navigation turns TOM Explorer's Show changes filter off before it walks the tree.</summary>
public sealed class ShowChangesGateTests
{
    private sealed class FakeCheckBox(bool? state, bool sticks = false)
    {
        public int Clicks { get; private set; }

        public Task<bool?> IsOn() => Task.FromResult(state);

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
    public async Task AFilterThatStaysOnIsReportedWithTheNextAction()
    {
        var box = new FakeCheckBox(true, sticks: true);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ShowChangesGate.TurnOff(box.IsOn, box.Click)
        );

        Assert.Contains("show-changes filter active", error.Message);
        Assert.Contains("Next:", error.Message);
        Assert.Equal(1, box.Clicks);
    }
}
