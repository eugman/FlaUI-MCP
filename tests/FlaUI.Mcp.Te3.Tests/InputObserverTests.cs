using FlaUI.Mcp.Core;
using Xunit;

/// <summary>
/// TE3's grant on Core's fails closed when its input observer dies, and a new batch starts a new observer; fakes only.
/// Core's own observer and heartbeat tests cover the rest.
/// </summary>
public sealed class InputObserverTests
{
    private sealed class FakeObserver : IInputObserver
    {
        public bool Dead { get; set; }
        public bool Disposed { get; private set; }

        public long Sequence =>
            Dead ? throw new InvalidOperationException("Physical input observer is unavailable") : 0;

        public InputCounts Counts => new(Sequence, 0, null);

        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void ADeadObserverIsReplacedWhenTheNextBatchBegins()
    {
        var started = new List<FakeObserver>();
        using var interaction = new Te3Interaction(() =>
        {
            var observer = new FakeObserver();
            started.Add(observer);
            return observer;
        });
        interaction.Begin("Open Preferences", userApproved: true);

        started[0].Dead = true;
        Assert.Throws<InputRefusedException>(interaction.Require);

        interaction.Begin("Open Preferences again", userApproved: true);
        interaction.Require();

        Assert.Equal(2, started.Count);
        Assert.True(started[0].Disposed);
    }
}
