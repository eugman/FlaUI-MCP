using Xunit;

/// <summary>
/// The TE3 polls stop on a poll count as well as on the injected clock (as Core's ClickTool.Settled does since
/// 7bb2691): with a clock that never moves and a pause that does not move it, Absence and the walk's AwaitMove used
/// to loop for ever. Each test bounds itself, so a regression fails instead of hanging.
/// </summary>
public sealed class StillClockTests
{
    private static readonly TimeSpan Still = TimeSpan.Zero;

    [Fact]
    public async Task AbsenceGivesUpAfterItsPollsOnAStillClock()
    {
        var reads = 0;

        // On the thread pool: on a still clock every step completes at once, so an uncapped loop never yields.
        var wait = Task.Run(() =>
            Absence.WaitFor(
                () =>
                {
                    reads++;
                    return Task.FromResult(true);
                },
                timeoutMs: 1000,
                settleMs: 50,
                () => Still,
                _ => Task.CompletedTask
            )
        );

        await Assert.ThrowsAsync<TimeoutException>(() => Within(wait));
        Assert.InRange(reads, 1, 1000 / Absence.PollMs + 2);
    }

    [Fact]
    public async Task AwaitMoveGivesUpAfterItsPollsOnAStillClock()
    {
        var reads = 0;

        var moved = await Within(
            Task.Run(() =>
                WalkClock.AwaitMove(
                    "row",
                    () =>
                    {
                        reads++;
                        return Task.FromResult("row");
                    },
                    () => Still,
                    _ => Task.CompletedTask
                )
            )
        );

        Assert.Null(moved);
        Assert.InRange(reads, 1, (int)(WalkClock.MoveTimeout.TotalMilliseconds / WalkClock.PollMs) + 2);
    }

    // A hang fails the test instead of stopping the run; Core's failure bound, only reached when the cap is missing.
    private static async Task<T> Within<T>(Task<T> task)
    {
        await Within((Task)task);
        return await task;
    }

    private static async Task Within(Task task)
    {
        var finished = await Task.WhenAny(task, Task.Delay(FlaUI.Mcp.Tests.Eventually.Bound));
        Assert.True(finished == task, "The poll looped on a still clock.");
        await task;
    }
}
