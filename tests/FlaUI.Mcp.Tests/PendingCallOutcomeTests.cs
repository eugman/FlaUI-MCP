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
    private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(100);

    [Fact]
    public void ALateFailureOfAPendingCallIsRecordedOnItsOperation()
    {
        var clock = new ManualTimeProvider();
        using var installed = new FakeDesktop { Clock = clock }.Install();
        var operation = new OperationContext();
        OperationContext.Current.Value = operation;
        using var hold = new ManualResetEventSlim();
        var tracker = new PendingInvokeTracker(_ => 1);
        try
        {
            var result = Eventually.PastDeadline(
                clock,
                Grace,
                () =>
                    ModalAwareInvoker.Execute(
                        Pid,
                        "Invoke on 'Save'",
                        () =>
                        {
                            hold.Wait();
                            throw new InvalidOperationException("mutation denied: the user is active");
                        },
                        tracker,
                        Grace,
                        _ => []
                    )
            );
            Assert.Equal(PatternCallOutcome.StillPending, result.Outcome);
        }
        finally
        {
            hold.Set();
            OperationContext.Current.Value = null;
        }

        Eventually.WaitUntil(() => !tracker.TryGetPending(Pid, out _), "the tracker to clear");
        Eventually.WaitUntil(() => operation.LateError != null, "the late error");
        Assert.Equal(
            "Invoke on 'Save' failed after it was reported pending: mutation denied: the user is active",
            operation.LateError
        );
    }

    [Fact]
    public void ACallStillInValidationWhenTheLeaseIsReleasedIsNeverSent()
    {
        var clock = new ManualTimeProvider();
        var desktop = new FakeDesktop { Clock = clock };
        using var installed = desktop.Install();
        var operation = new OperationContext();
        OperationContext.Current.Value = operation;
        using var hold = new ManualResetEventSlim();
        var tracker = new PendingInvokeTracker(_ => 1);
        var sent = false;
        try
        {
            var result = Eventually.PastDeadline(
                clock,
                Grace,
                () =>
                    WindowMutation.Uia(
                        Pid,
                        "Invoke on 'Save'",
                        () => sent = true,
                        tracker,
                        validate: hold.Wait,
                        gracePeriod: Grace
                    )
            );
            Assert.Equal(PatternCallOutcome.StillPending, result.Outcome);
            Assert.False(result.Dispatched);
        }
        finally
        {
            hold.Set();
            OperationContext.Current.Value = null;
        }

        Eventually.WaitUntil(() => !tracker.TryGetPending(Pid, out _), "the tracker to clear");
        Assert.False(sent);
        Assert.Equal(0, desktop.PatternCalls);
        Assert.Equal(0, operation.InputsSent);
    }

    [Fact]
    public void ACallHeldInThePermissionCheckWhenTheLeaseIsReleasedIsNeverSent()
    {
        var clock = new ManualTimeProvider();
        var desktop = new FakeDesktop { Clock = clock };
        using var installed = desktop.Install();
        var operation = new OperationContext();
        OperationContext.Current.Value = operation;
        using var hold = new ManualResetEventSlim();
        MutationGuard.BeforeMutation.Value = () => hold.Wait();
        var tracker = new PendingInvokeTracker(_ => 1);
        var sent = false;
        try
        {
            var result = Eventually.PastDeadline(
                clock,
                Grace,
                () => WindowMutation.Uia(Pid, "Invoke on 'Save'", () => sent = true, tracker, gracePeriod: Grace)
            );
            Assert.Equal(PatternCallOutcome.StillPending, result.Outcome);
            Assert.False(result.Dispatched);
        }
        finally
        {
            hold.Set();
            MutationGuard.BeforeMutation.Value = null;
            OperationContext.Current.Value = null;
        }

        Eventually.WaitUntil(() => !tracker.TryGetPending(Pid, out _), "the tracker to clear");
        Assert.False(sent);
        Assert.Equal(0, desktop.PatternCalls);
        Assert.Equal(0, operation.InputsSent);
    }

    [Fact]
    public void AFailedWindowReadWhileWaitingDoesNotAbandonTheCall()
    {
        // The call returns only once the wait has seen a failed window read, so the wait must go on past it.
        using var readFailed = new ManualResetEventSlim();
        var reads = 0;
        var result = ModalAwareInvoker.Execute(
            Pid,
            "Invoke on 'Save'",
            () => readFailed.Wait(Eventually.Bound),
            new PendingInvokeTracker(_ => 1),
            TimeSpan.FromSeconds(10),
            _ =>
            {
                if (++reads == 1)
                {
                    return [];
                }

                readFailed.Set();
                throw new System.ComponentModel.Win32Exception(5);
            }
        );

        Assert.Equal(PatternCallOutcome.Completed, result.Outcome);
    }

    [Fact]
    public void APendingCallWhoseWindowReadsFailedSaysSoInItsReply()
    {
        var clock = new ManualTimeProvider();
        using var installed = new FakeDesktop { Clock = clock }.Install();
        using var hold = new ManualResetEventSlim();
        var reads = 0;
        try
        {
            var result = Eventually.PastDeadline(
                clock,
                Grace,
                () =>
                    ModalAwareInvoker.Execute(
                        Pid,
                        "Invoke on 'Save'",
                        hold.Wait,
                        new PendingInvokeTracker(_ => 1),
                        Grace,
                        _ => ++reads == 1 ? [] : throw new TimeoutException("window list timed out")
                    )
            );

            Assert.Equal(PatternCallOutcome.StillPending, result.Outcome);
            Assert.Equal("window list timed out", result.WaitError);
            var reply = FlaUI.Mcp.Tools.ClickTool.PatternResult(result, "Invoked Save");
            Assert.Contains(
                "While waiting, reading the app's windows failed (window list timed out), so a modal dialog may have gone undetected.",
                reply.Content[0].Text
            );
        }
        finally
        {
            hold.Set();
        }
    }
}
