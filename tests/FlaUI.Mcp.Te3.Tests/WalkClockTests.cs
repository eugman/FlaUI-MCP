using Xunit;

/// <summary>The object walk's time limits over a fake clock; no desktop access.</summary>
public sealed class WalkClockTests
{
    [Fact]
    public async Task DeadlineRefusesInputAfterAReadCrossesTheWalkStop()
    {
        var inputs = 0;

        await Assert.ThrowsAsync<TimeoutException>(() =>
            WalkClock.BeforeInput(
                WalkClock.OutOfTime(() => TimeSpan.FromSeconds(18.1)),
                () =>
                {
                    inputs++;
                    return Task.CompletedTask;
                },
                "type TOM Explorer search"
            )
        );

        Assert.Equal(0, inputs);
    }

    private sealed class SlowTree(Action onDown, params RowObservation[] rows) : ITreeCursor
    {
        private int at;
        public int Downs { get; private set; }

        public Task<RowObservation> Current() => Task.FromResult(rows[at]);

        public Task<RowObservation?> Down()
        {
            Downs++;
            onDown();
            at = Math.Min(at + 1, rows.Length - 1);
            return Task.FromResult<RowObservation?>(rows[at]);
        }
    }

    [Fact]
    public async Task TheWalkStopsOnTotalTimeSinceTheToolStartedNotSinceTheWalkStarted()
    {
        // Opening TOM Explorer, the Show changes gate, the typed search and Home took 15 s before the first Down.
        var now = TimeSpan.FromSeconds(15);
        var rows = Enumerable.Range(1, 30).Select(i => new RowObservation($"Measure {i}", "Measure")).ToArray();
        var tree = new SlowTree(() => now += TimeSpan.FromSeconds(1), [new("Brands", "Table"), .. rows]);

        var walk = await PathWalker.Find(tree, "Brands", "Brand", "Column", outOfTime: WalkClock.OutOfTime(() => now));

        Assert.False(walk.Found);
        Assert.Equal(4, tree.Downs);
        Assert.Contains("before the tool deadline", walk.Reason);
    }

    [Fact]
    public void TheWalkLeavesTimeForTheFinalClear()
    {
        Assert.False(WalkClock.OutOfTime(() => TimeSpan.FromSeconds(17.9))());
        Assert.True(WalkClock.OutOfTime(() => TimeSpan.FromSeconds(18.1))());
        Assert.True(TimeSpan.FromSeconds(30) - WalkClock.WalkStop >= TimeSpan.FromSeconds(6) + WalkClock.MoveTimeout);
    }

    [Fact]
    public async Task AMoveIsBoundedByElapsedTimeIncludingItsReads()
    {
        // Each read took 2 s; summing only the 60 ms delays allowed 25 reads (50 s) before giving up.
        var now = TimeSpan.Zero;
        var reads = 0;

        var moved = await WalkClock.AwaitMove(
            "row 1",
            () =>
            {
                reads++;
                now += TimeSpan.FromSeconds(2);
                return Task.FromResult("row 1");
            },
            () => now,
            _ => Task.CompletedTask
        );

        Assert.Null(moved);
        Assert.Equal(1, reads);
    }

    [Fact]
    public async Task AMoveIsTakenOnceASecondReadAgrees()
    {
        var now = TimeSpan.Zero;
        var answers = new Queue<string>(["row 1", "row 2", "row 2"]);

        var moved = await WalkClock.AwaitMove(
            "row 1",
            () =>
            {
                now += TimeSpan.FromMilliseconds(100);
                return Task.FromResult(answers.Dequeue());
            },
            () => now,
            _ => Task.CompletedTask
        );

        Assert.Equal("row 2", moved);
    }
}
