using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Tests;

public class ModalAwareInvokerTests
{
    private const int TestProcessId = 1234;

    /// <summary>A desktop whose clock never moves unless the test moves it, so no grace period passes on its own.</summary>
    private static (ManualTimeProvider Clock, IDisposable Installed) StillClock()
    {
        var clock = new ManualTimeProvider();
        return (clock, new FakeDesktop { Clock = clock }.Install());
    }

    private static Win32WindowInfo MakeWindow(nint hwnd, string title, bool enabled = true) =>
        new(hwnd, title, TestProcessId, enabled, IsToolWindow: false, IsCloaked: false);

    [Fact]
    public void Execute_CompletedAction_ReturnsCompleted_AndClearsTracker()
    {
        var tracker = new PendingInvokeTracker();
        var windows = new List<Win32WindowInfo> { MakeWindow(1, "Main") };

        var result = ModalAwareInvoker.Execute(
            TestProcessId,
            "Invoke on 'OK'",
            () => { },
            tracker,
            windowEnumerator: _ => windows
        );

        Assert.Equal(PatternCallOutcome.Completed, result.Outcome);

        // The tracker is cleared by a task continuation; allow it a moment
        Eventually.WaitUntil(() => !tracker.TryGetPending(TestProcessId, out _), "the tracker to clear");
    }

    [Fact]
    public void Execute_FaultedAction_RethrowsException()
    {
        var tracker = new PendingInvokeTracker();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ModalAwareInvoker.Execute(
                TestProcessId,
                "Invoke on 'OK'",
                () => throw new InvalidOperationException("boom"),
                tracker,
                windowEnumerator: _ => new List<Win32WindowInfo>()
            )
        );

        Assert.Equal("boom", ex.Message);
        Eventually.WaitUntil(() => !tracker.TryGetPending(TestProcessId, out _), "the tracker to clear");
    }

    [Fact]
    public void Execute_BlockingActionWithNewWindow_ReturnsModalDetected()
    {
        var tracker = new PendingInvokeTracker();
        using var release = new ManualResetEventSlim(false);
        var callCount = 0;

        // First enumeration: only the main window. Later enumerations: a new
        // dialog window appeared while the action is still blocking.
        IReadOnlyList<Win32WindowInfo> Enumerate(int _)
        {
            var windows = new List<Win32WindowInfo> { MakeWindow(1, "Main", enabled: callCount == 0) };
            if (Interlocked.Increment(ref callCount) > 1)
            {
                windows.Add(MakeWindow(2, "Test Modal Dialog") with { Owner = 1 });
            }

            return windows;
        }

        var (_, installed) = StillClock();
        using var desktop = installed;
        try
        {
            // The clock never moves, so returning at all means the modal ended the wait, not the grace period.
            var result = Eventually.Returns(() =>
                ModalAwareInvoker.Execute(
                    TestProcessId,
                    "Invoke on 'Open Modal'",
                    () => release.Wait(),
                    tracker,
                    gracePeriod: TimeSpan.FromSeconds(10),
                    windowEnumerator: Enumerate
                )
            );

            Assert.Equal(PatternCallOutcome.ModalDetected, result.Outcome);
            Assert.Equal("Test Modal Dialog", result.ModalTitle);

            // The call is still pending and the tracker knows about the modal
            Assert.True(tracker.TryGetPending(TestProcessId, out var pending));
            Assert.Equal("Test Modal Dialog", pending.ModalTitle);
        }
        finally
        {
            release.Set();
        }

        // Once the action returns, the tracker clears
        Eventually.WaitUntil(() => !tracker.TryGetPending(TestProcessId, out _), "the tracker to clear");
    }

    [Fact]
    public void Execute_BlockingActionOwnerDisabledOnly_ReturnsModalDetected()
    {
        var tracker = new PendingInvokeTracker();
        using var release = new ManualResetEventSlim(false);
        var callCount = 0;

        // The owner window becomes disabled but no new window is enumerated
        // (e.g. the dialog is a tool window filtered elsewhere).
        IReadOnlyList<Win32WindowInfo> Enumerate(int _)
        {
            var enabled = Interlocked.Increment(ref callCount) == 1;
            return new List<Win32WindowInfo> { MakeWindow(1, "Main", enabled) };
        }

        var (_, installed) = StillClock();
        using var desktop = installed;
        try
        {
            // Disabled-owner detection must not wait for the grace period, which never passes on this clock.
            var result = Eventually.Returns(() =>
                ModalAwareInvoker.Execute(
                    TestProcessId,
                    "Invoke on 'Open Modal'",
                    () => release.Wait(),
                    tracker,
                    gracePeriod: TimeSpan.FromSeconds(10),
                    windowEnumerator: Enumerate
                )
            );

            Assert.Equal(PatternCallOutcome.ModalDetected, result.Outcome);
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public void Execute_BlockingActionNoWindowChange_ReturnsStillPendingAfterGrace()
    {
        var tracker = new PendingInvokeTracker();
        using var release = new ManualResetEventSlim(false);
        var windows = new List<Win32WindowInfo> { MakeWindow(1, "Main") };

        var (clock, installed) = StillClock();
        using var desktop = installed;
        try
        {
            var call = Task.Run(() =>
                ModalAwareInvoker.Execute(
                    TestProcessId,
                    "Invoke on 'Slow'",
                    () => release.Wait(),
                    tracker,
                    // Long enough that the poll cap (one 50 ms poll per 50 ms of grace) cannot end it first.
                    gracePeriod: TimeSpan.FromSeconds(10),
                    windowEnumerator: _ => windows
                )
            );

            // The wait reads the clock once for its deadline, then once a turn.
            Eventually.WaitUntil(() => clock.NowReads >= 3, "the wait to poll");
            clock.Advance(TimeSpan.FromSeconds(10) - TimeSpan.FromMilliseconds(1));
            var reads = clock.NowReads;
            Eventually.WaitUntil(() => clock.NowReads >= reads + 2, "the wait to poll 1 ms before its deadline");
            Assert.False(call.IsCompleted, "Returned before the grace period passed");
            clock.Advance(TimeSpan.FromMilliseconds(1));
            var result = Eventually.Result(call);

            Assert.Equal(PatternCallOutcome.StillPending, result.Outcome);
            Assert.True(tracker.TryGetPending(TestProcessId, out _));
        }
        finally
        {
            release.Set();
        }

        Eventually.WaitUntil(() => !tracker.TryGetPending(TestProcessId, out _), "the tracker to clear");
    }

    [Fact]
    public void AHungCallOnAStillClockStopsAfterItsGracePeriodsPolls()
    {
        // No modal and a clock nobody moves: only the poll cap ends the wait, after the grace period's 50 ms polls.
        var tracker = new PendingInvokeTracker();
        using var release = new ManualResetEventSlim(false);
        var (_, installed) = StillClock();
        using var desktop = installed;
        var reads = 0;
        try
        {
            var result = Eventually.Returns(() =>
                ModalAwareInvoker.Execute(
                    TestProcessId,
                    "Invoke on 'Hung'",
                    () => release.Wait(),
                    tracker,
                    gracePeriod: TimeSpan.FromMilliseconds(300),
                    windowEnumerator: _ =>
                    {
                        Interlocked.Increment(ref reads);
                        return [MakeWindow(1, "Main")];
                    }
                )
            );

            Assert.Equal(PatternCallOutcome.StillPending, result.Outcome);
            // The baseline read, then one read after each of the six 50 ms polls of the 300 ms grace period.
            Assert.Equal(7, reads);
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public void Execute_UnknownProcessId_SkipsModalDetection()
    {
        var tracker = new PendingInvokeTracker();
        var enumeratorCalled = false;
        var (clock, installed) = StillClock();
        using var desktop = installed;

        // The call returns only after the wait has polled a few times, each a chance to read windows it must not.
        var result = ModalAwareInvoker.Execute(
            0,
            "Invoke on 'OK'",
            () => Eventually.WaitUntil(() => clock.NowReads >= 3, "the wait to poll"),
            tracker,
            gracePeriod: TimeSpan.FromMilliseconds(600),
            windowEnumerator: _ =>
            {
                enumeratorCalled = true;
                return new List<Win32WindowInfo>();
            }
        );

        Assert.Equal(PatternCallOutcome.Completed, result.Outcome);
        Assert.False(enumeratorCalled);
    }
}
