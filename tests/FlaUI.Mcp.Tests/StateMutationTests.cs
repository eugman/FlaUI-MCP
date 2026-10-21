using System.Runtime.CompilerServices;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>Tests written to kill mutants that survived in the ref, operation and pending-call state.</summary>
public sealed class StateMutationTests
{
    private static AutomationElement Element() =>
        (AutomationElement)RuntimeHelpers.GetUninitializedObject(typeof(AutomationElement));

    private static T InCancelledOperation<T>(Func<T> run)
    {
        var operation = new OperationContext();
        operation.Stop.Cancel();
        OperationContext.Current.Value = operation;
        try
        {
            return run();
        }
        finally
        {
            OperationContext.Current.Value = null;
        }
    }

    [Fact]
    public void ASnapshotDropsOnlyItsOwnWindowsRefs()
    {
        var refs = new ElementRegistry();
        var other = refs.Register("w2", Element());
        refs.Register("w1", Element());

        refs.BeginSnapshot("w1");

        Assert.True(refs.HasElement(other));
        Assert.False(refs.HasElement("w1e1"));
    }

    [Fact]
    public void RegisteringInACancelledOperationIsRefused()
    {
        var refs = new ElementRegistry();

        Assert.Throws<OperationCanceledException>(() => InCancelledOperation(() => refs.Register("w1", Element())));
        Assert.False(refs.HasElement("w1e1"));
    }

    [Fact]
    public void RecordingAProcessInACancelledOperationIsRefused()
    {
        var refs = new ElementRegistry();

        Assert.Throws<OperationCanceledException>(() =>
            InCancelledOperation(() =>
            {
                refs.SetWindowProcessId("w1", 42);
                return 0;
            })
        );
        Assert.Equal(0, refs.GetProcessIdForRef("w1e1"));
    }

    [Fact]
    public void ASupersededSnapshotSaysToRefreshRefs()
    {
        var refs = new ElementRegistry();
        var first = refs.BeginSnapshot("w1");
        refs.BeginSnapshot("w1");

        var error = Assert.Throws<OperationCanceledException>(() => refs.Register("w1", Element(), first));

        Assert.Equal("Snapshot superseded by a newer observation; refresh refs.", error.Message);
    }

    [Fact]
    public void ValidatingARefResolvesItFirst()
    {
        var refs = new ElementRegistry();
        var reference = refs.Register("w1", Element());

        var unknown = Assert.Throws<ArgumentException>(() => refs.ValidateReference("w1e9"));
        var elsewhere = Assert.Throws<ArgumentException>(() => refs.ValidateReference(reference, "w2"));
        var unowned = Assert.Throws<InvalidOperationException>(() => refs.ValidateReference(reference));

        Assert.StartsWith("Element not found: w1e9.", unknown.Message);
        Assert.Equal("Ref w1e1 belongs to window w1, not w2.", elsewhere.Message);
        Assert.EndsWith("Refresh it with windows_snapshot or windows_find on a window handle.", unowned.Message);
    }

    [Fact]
    public void AnUnknownRefIsNamedAsSuch()
    {
        Assert.Equal(
            "Unknown element ref.",
            Assert.Throws<ArgumentException>(() => new ElementRegistry().WindowForRef("w1e1")).Message
        );
    }

    [Fact]
    public void PruningDropsTheOldestFinishedOperation()
    {
        var operations = new OperationCoordinator();
        var oldest = operations.Begin("oldest");
        oldest.Finished = true;
        Thread.Sleep(30);
        for (var i = 0; i < 100; i++)
        {
            operations.Begin("done").Finished = true;
        }

        operations.Begin("next");

        var ids = JsonSerializer
            .SerializeToElement(operations.Status())
            .EnumerateArray()
            .Select(o => o.GetProperty("operationId").GetString())
            .ToArray();
        Assert.DoesNotContain(oldest.Id, ids);
        Assert.Equal(101, ids.Length);
    }

    [Fact]
    public void StatusListsTheNewestOperationFirst()
    {
        var operations = new OperationCoordinator();
        operations.Begin("first");
        Thread.Sleep(30);
        var second = operations.Begin("second");

        var first = JsonSerializer.SerializeToElement(operations.Status()).EnumerateArray().First();

        Assert.Equal(second.Id, first.GetProperty("operationId").GetString());
    }

    [Fact]
    public void OperationsHaveDistinctIdsAndNoToolUntilNamed()
    {
        var one = new OperationContext();
        var two = new OperationContext();

        Assert.Equal(32, one.Id.Length);
        Assert.NotEqual(one.Id, two.Id);
        Assert.Equal("", one.Tool);
    }

    [Fact]
    public void StatusWithoutATrackerSaysTheProviderIsNotTracked()
    {
        var operations = new OperationCoordinator();
        var op = operations.Begin("tool");

        var status = JsonSerializer.SerializeToElement(operations.Status(op.Id)).EnumerateArray().Single();

        Assert.Equal("not-tracked", status.GetProperty("provider").GetString());
        Assert.Equal(JsonValueKind.Array, status.GetProperty("pendingPatternIds").ValueKind);
        Assert.Empty(status.GetProperty("pendingPatternIds").EnumerateArray());
    }

    [Fact]
    public void ABlockedDescriptionMentionsAModalOnlyWhenThereIsOne()
    {
        var info = new PendingInvokeTracker().Begin(42, "Invoke on 'OK'");

        var message = PendingInvokeTracker.DescribeBlocked(info);

        Assert.DoesNotContain("opened modal", message);
        Assert.Contains(
            "Ref tools on this app fail until it returns. Find the dialog with windows_list_windows, then use windows_screenshot",
            message
        );
    }

    [Fact]
    public void PendingCallsHaveDistinctIdsAndReportPending()
    {
        var tracker = new PendingInvokeTracker(_ => 1);
        var one = tracker.Begin(42, "a");
        var two = tracker.Begin(42, "b");

        Assert.Equal(32, one.OperationId.Length);
        Assert.NotEqual(one.OperationId, two.OperationId);
        Assert.All(
            JsonSerializer.SerializeToElement(tracker.Status()).EnumerateArray(),
            p => Assert.Equal("pending", p.GetProperty("status").GetString())
        );
    }

    [Fact]
    public void ALiveProcessStaysBlockedWhileItsCallIsPending()
    {
        var tracker = new PendingInvokeTracker();
        tracker.Begin(Environment.ProcessId, "Invoke on 'OK'");

        Assert.True(tracker.TryGetPending(Environment.ProcessId, out _));
    }

    [Fact]
    public void AnUnknownProcessIsNeverInspected()
    {
        var inspected = new List<int>();
        var tracker = new PendingInvokeTracker(pid =>
        {
            inspected.Add(pid);
            return 1;
        });

        tracker.TryGetPending(0, out _);

        Assert.Empty(inspected);
    }

    [Fact]
    public void ASnapshotKeepsTheRefsOfAWindowWhoseHandleItPrefixes()
    {
        var refs = new ElementRegistry();
        var w10 = refs.Register("w10", Element());

        refs.BeginSnapshot("w1");

        Assert.True(refs.HasElement(w10));
    }

    [Fact]
    public void ABlockedDescriptionWithoutAModalGoesStraightToTheAdvice()
    {
        var info = new PendingInvokeTracker().Begin(42, "Invoke on 'OK'");

        Assert.Matches(
            @"\('Invoke on 'OK'', \d+s\)\. Ref tools on this app",
            PendingInvokeTracker.DescribeBlocked(info)
        );
    }
}
