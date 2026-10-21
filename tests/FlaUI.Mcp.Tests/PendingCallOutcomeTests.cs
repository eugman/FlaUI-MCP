using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// A pattern call reported as pending keeps its ending: a late failure is recorded on the operation instead
/// of reading as idle, a call still in validation can no longer go out once the lease is released, and a
/// failed window read while waiting does not abandon the wait.
/// </summary>
[Collection(InputLeaseCollection.Name)]
public sealed class PendingCallOutcomeTests
{
    private const int Pid = 4321;

    private static void WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition did not hold within 10 s.");
            Thread.Sleep(10);
        }
    }

    [Fact]
    public void ALateFailureOfAPendingCallIsRecordedOnItsOperation()
    {
        var operation = new OperationContext();
        OperationContext.Current.Value = operation;
        using var hold = new ManualResetEventSlim();
        var tracker = new PendingInvokeTracker(_ => 1);
        try
        {
            var result = ModalAwareInvoker.Execute(
                Pid,
                "Invoke on 'Save'",
                () =>
                {
                    hold.Wait();
                    throw new InvalidOperationException("mutation denied: the user is active");
                },
                tracker,
                TimeSpan.FromMilliseconds(100),
                _ => []
            );
            Assert.Equal(PatternCallOutcome.StillPending, result.Outcome);
        }
        finally
        {
            hold.Set();
            OperationContext.Current.Value = null;
        }

        WaitUntil(() => !tracker.TryGetPending(Pid, out _));
        WaitUntil(() => operation.LateError != null);
        Assert.Equal(
            "Invoke on 'Save' failed after it was reported pending: mutation denied: the user is active",
            operation.LateError
        );
    }

    [Fact]
    public void ACallStillInValidationWhenTheLeaseIsReleasedIsNeverSent()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        var operation = new OperationContext();
        OperationContext.Current.Value = operation;
        using var hold = new ManualResetEventSlim();
        var tracker = new PendingInvokeTracker(_ => 1);
        var sent = false;
        try
        {
            var result = WindowMutation.Uia(
                Pid,
                "Invoke on 'Save'",
                () => sent = true,
                tracker,
                validate: hold.Wait,
                gracePeriod: TimeSpan.FromMilliseconds(100)
            );
            Assert.Equal(PatternCallOutcome.StillPending, result.Outcome);
            Assert.False(result.Dispatched);
        }
        finally
        {
            hold.Set();
            OperationContext.Current.Value = null;
        }

        WaitUntil(() => !tracker.TryGetPending(Pid, out _));
        Assert.False(sent);
        Assert.Equal(0, desktop.PatternCalls);
        Assert.Equal(0, operation.InputsSent);
    }

    [Fact]
    public void AFailedWindowReadWhileWaitingDoesNotAbandonTheCall()
    {
        var reads = 0;
        var result = ModalAwareInvoker.Execute(
            Pid,
            "Invoke on 'Save'",
            () => Task.Delay(200).Wait(),
            new PendingInvokeTracker(_ => 1),
            TimeSpan.FromSeconds(5),
            _ => ++reads == 1 ? [] : throw new System.ComponentModel.Win32Exception(5)
        );

        Assert.Equal(PatternCallOutcome.Completed, result.Outcome);
    }

    [Fact]
    public void AWaitThatTimesOutKeepsItsLastReadError()
    {
        var error = new TimeoutException("provider busy");
        WaitOutcome<int> outcome = new WaitOutcome<int>.TimedOut(3, error);

        var timedOut = Assert.IsType<WaitOutcome<int>.TimedOut>(outcome);
        Assert.Equal(3, timedOut.Last);
        Assert.Same(error, timedOut.LastError);
    }
}
