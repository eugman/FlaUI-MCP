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
        Assert.Throws<InvalidOperationException>(interaction.Require);

        interaction.Begin("Open Preferences again", userApproved: true);
        interaction.Require();

        Assert.Equal(2, started.Count);
        Assert.True(started[0].Disposed);
    }

    [Theory]
    [InlineData(4_900, 5_000, true)]
    [InlineData(0, 5_000, false)]
    public void AHookThreadThatStoppedBeatingIsNotTrusted(long lastBeat, long now, bool responsive) =>
        Assert.Equal(responsive, PhysicalInputObserver.IsResponsive(lastBeat, now));
}
