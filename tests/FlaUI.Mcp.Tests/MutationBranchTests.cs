using System.Text.Json;
using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>Window mutations without a native handle, and how operations report themselves.</summary>
[Collection(InputLeaseCollection.Name)]
public sealed class MutationBranchTests
{
    private static int Pid => FakeDesktop.TargetPid;

    [Fact]
    public void NativeFocusOfAnAllowedWindowFocusesIt()
    {
        var desktop = new FakeDesktop { ForegroundWindow = FakeDesktop.OtherHwnd };
        using var installed = desktop.Install();

        WindowMutation.Focus(FakeDesktop.Target, FakeDesktop.TargetHwnd);

        Assert.Equal(1, desktop.FocusAttempts);
        Assert.Equal(FakeDesktop.TargetHwnd, desktop.ForegroundWindow);
    }

    [Fact]
    public void UiaMutationIsRefusedWhileTheProviderIsBlocked()
    {
        var tracker = new PendingInvokeTracker();
        tracker.Begin(Pid, "Invoke on 'Open...'");
        var called = false;

        var error = Assert.Throws<ProviderBlockedException>(() =>
            WindowMutation.Uia(Pid, "Focus on w1", () => called = true, tracker)
        );

        Assert.Equal("Invoke on 'Open...'", error.Pending.Description);
        Assert.False(called);
    }

    [Fact]
    public void UiaMutationIsRefusedForAHigherIntegrityProcess()
    {
        var desktop = new FakeDesktop { TargetIntegrity = new(ProcessIntegrity.High, false) };
        using var installed = desktop.Install();
        var called = false;

        var error = Assert.Throws<InputRefusedException>(() =>
            WindowMutation.Uia(Pid, "Focus on w1", () => called = true, null)
        );

        Assert.Equal("integrity-blocked", error.Refusal.Code);
        Assert.False(called);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UiaMutationRunsTheCallUnderTheInputLease(bool tracked)
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        var leaseHeld = false;

        WindowMutation.Uia(
            Pid,
            "Focus on w1",
            () => leaseHeld = LeaseHeld(),
            tracked ? new PendingInvokeTracker() : null
        );

        Assert.True(leaseHeld);
    }

    [Fact]
    public void RefuseIfBlockedPassesWhenNothingIsPending()
    {
        Assert.Null(Record.Exception(() => WindowMutation.RefuseIfBlocked(Pid, new PendingInvokeTracker())));
        Assert.Null(Record.Exception(() => WindowMutation.RefuseIfBlocked(Pid, null)));
    }

    private static bool LeaseHeld()
    {
        var gate = (SemaphoreSlim)
            typeof(GuardedInput)
                .GetField(
                    "InputGate",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static
                )!
                .GetValue(null)!;
        return gate.CurrentCount == 0;
    }
}

public sealed class OperationStatusTests
{
    private static JsonElement Only(OperationCoordinator operations, string id) =>
        JsonSerializer.SerializeToElement(operations.Status(id)).EnumerateArray().Single();

    [Fact]
    public void ARunningOperationWithNoInputIsUnknownDispatch()
    {
        var operations = new OperationCoordinator();
        var op = operations.Begin("tool");

        var status = Only(operations, op.Id);

        Assert.Equal("running", status.GetProperty("status").GetString());
        Assert.Equal("unknown", status.GetProperty("dispatch").GetString());
    }

    [Fact]
    public void ACancelledOperationThatSentInputIsTimedOutPendingAndPartial()
    {
        var operations = new OperationCoordinator();
        var op = operations.Begin("tool");
        op.RecordInput();
        op.Stop.Cancel();

        var status = Only(operations, op.Id);

        Assert.Equal("timed_out_pending", status.GetProperty("status").GetString());
        Assert.Equal("partial", status.GetProperty("dispatch").GetString());
        Assert.Equal(1, status.GetProperty("inputsSent").GetInt32());
    }

    [Fact]
    public void OnlyTheNewestHundredFinishedOperationsAreKept()
    {
        var operations = new OperationCoordinator();
        var running = operations.Begin("still running");
        for (var i = 0; i < 101; i++)
        {
            operations.Begin("done").Finished = true;
        }

        operations.Begin("next");

        var all = JsonSerializer.SerializeToElement(operations.Status()).EnumerateArray().ToArray();
        Assert.Equal(102, all.Length);
        Assert.Equal(100, all.Count(o => o.GetProperty("finished").GetBoolean()));
        Assert.Contains(all, o => o.GetProperty("operationId").GetString() == running.Id);
    }

    [Theory]
    [InlineData(ProcessIntegrity.SystemLevel, "system")]
    [InlineData(ProcessIntegrity.High, "high")]
    [InlineData(ProcessIntegrity.Medium, "medium")]
    [InlineData(ProcessIntegrity.Low, "low")]
    [InlineData(ProcessIntegrity.Low - 1, "untrusted")]
    [InlineData(0, "untrusted")]
    public void IntegrityLevelsAreNamedByTheirLowerBound(int level, string name)
    {
        Assert.Equal(name, new ProcessIntegrity(level, false).LevelName);
    }
}
