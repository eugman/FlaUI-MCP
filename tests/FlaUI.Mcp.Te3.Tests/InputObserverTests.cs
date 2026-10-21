using Xunit;

/// <summary>The grant fails closed when its input observer dies, and a new batch starts a new observer; fakes only.</summary>
public sealed class InputObserverTests
{
    private sealed class FakeObserver : IInputObserver
    {
        public bool Dead { get; set; }
        public bool Disposed { get; private set; }

        public long Sequence =>
            Dead ? throw new InvalidOperationException("Physical input observer is unavailable") : 0;

        public InputCounts Counts => new(Sequence, 0);

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
        Assert.ThrowsAny<InvalidOperationException>(interaction.Require);

        interaction.Begin("Open Preferences again", userApproved: true);
        interaction.Require();

        Assert.Equal(2, started.Count);
        Assert.True(started[0].Disposed);
    }

    [Fact]
    public void AMissedHeartbeatIsNeverForgiven()
    {
        // At or below Windows' LowLevelHooksTimeout: a gap that long may already have cost the hooks.
        var heartbeat = new Heartbeat(0);
        heartbeat.Beat(250);
        Assert.True(heartbeat.Alive(900));

        Assert.False(heartbeat.Alive(1_300));
        heartbeat.Beat(1_350);

        Assert.False(heartbeat.Alive(1_400));
    }

    [Fact]
    public void ALateBeatStillPoisonsTheObserver()
    {
        var heartbeat = new Heartbeat(0);

        heartbeat.Beat(1_200);

        Assert.False(heartbeat.Alive(1_250));
    }
}
