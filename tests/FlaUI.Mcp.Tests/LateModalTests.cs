using FlaUI.Mcp;
using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// A pattern call's modal check ran only while the call ran, and a toggle's state was read back at once, so a
/// dialog that opened a moment later (PBI's F12 toggle did) came after a "verified" reply. A completed call is now
/// watched a bounded moment more on the desktop's clock, and a verdict read before such a dialog is unsettled.
/// </summary>
public sealed class LateModalTests
{
    private const int ProcessId = 1234;

    private static Win32WindowInfo Window(nint hwnd, string title, bool enabled = true) =>
        new(hwnd, title, ProcessId, enabled, IsToolWindow: false, IsCloaked: false);

    private static (FakeDesktop Desktop, ManualTimeProvider Clock) StillDesktop()
    {
        var clock = new ManualTimeProvider();
        return (new FakeDesktop { Clock = clock }, clock);
    }

    [Fact]
    public void ADialogThatOpensAfterTheCallReturnedIsReportedOnTheCompletedResult()
    {
        var (desktop, clock) = StillDesktop();
        using var installed = desktop.Install();
        var started = clock.GetTimestamp();
        var returned = false;
        var reads = 0;
        IReadOnlyList<Win32WindowInfo> Enumerate(int _)
        {
            // Two reads after the call returned (100 ms), the dialog is up and its owner disabled.
            var late = returned && ++reads >= 2;
            var windows = new List<Win32WindowInfo> { Window(1, "Main", enabled: !late) };
            if (late)
            {
                windows.Add(Window(2, "Save As") with { Owner = 1 });
            }

            return windows;
        }

        var result = ModalAwareInvoker.Execute(
            ProcessId,
            "Toggle on 'F12'",
            () => returned = true,
            new PendingInvokeTracker(),
            windowEnumerator: Enumerate
        );

        Assert.Equal(PatternCallOutcome.Completed, result.Outcome);
        Assert.Equal("Save As", result.ModalTitle);
        Assert.Equal(TimeSpan.FromMilliseconds(100), clock.GetElapsedTime(started));
    }

    [Fact]
    public void TheWatchIsBoundedByTheGraceOnTheDesktopsClock()
    {
        var (desktop, clock) = StillDesktop();
        using var installed = desktop.Install();
        var started = clock.GetTimestamp();
        var reads = 0;

        var result = ModalAwareInvoker.Execute(
            ProcessId,
            "Toggle on 'F12'",
            () => { },
            new PendingInvokeTracker(),
            windowEnumerator: _ =>
            {
                reads++;
                return [Window(1, "Main")];
            }
        );

        Assert.Equal(PatternCallOutcome.Completed, result.Outcome);
        Assert.Null(result.ModalTitle);
        Assert.Equal(ModalAwareInvoker.LateModalGrace, clock.GetElapsedTime(started));
        // The baseline, then one read per poll of the grace.
        Assert.Equal(1 + 6, reads);
    }

    [Fact]
    public void AFailedWindowReadDuringTheWatchIsNotADialog()
    {
        var (desktop, _) = StillDesktop();
        using var installed = desktop.Install();
        var returned = false;

        var result = ModalAwareInvoker.Execute(
            ProcessId,
            "Toggle on 'F12'",
            () => returned = true,
            new PendingInvokeTracker(),
            windowEnumerator: _ => returned ? throw new InvalidOperationException("gone") : [Window(1, "Main")]
        );

        Assert.Equal(PatternCallOutcome.Completed, result.Outcome);
        Assert.Null(result.ModalTitle);
    }

    [Theory]
    [InlineData("SetValue")]
    [InlineData("ScrollIntoView")]
    [InlineData("Focus on 'Name'")]
    public void ACallThatCannotOpenADialogIsNotWatched(string description)
    {
        var (desktop, clock) = StillDesktop();
        using var installed = desktop.Install();
        var started = clock.GetTimestamp();
        var reads = 0;

        var result = ModalAwareInvoker.Execute(
            ProcessId,
            description,
            () => { },
            new PendingInvokeTracker(),
            windowEnumerator: _ =>
            {
                reads++;
                return [Window(1, "Main")];
            }
        );

        Assert.Equal(PatternCallOutcome.Completed, result.Outcome);
        Assert.Null(result.ModalTitle);
        // The baseline only: these known non-modal calls skip the watch.
        Assert.Equal(1, reads);
        Assert.Equal(TimeSpan.Zero, clock.GetElapsedTime(started));
    }

    [Theory]
    [InlineData("Invoke on 'Save'")]
    [InlineData("Select on 'Row'")]
    [InlineData("Expand on 'Node'")]
    [InlineData("Close on 'Main'")]
    [InlineData("CustomPattern on 'Action'")]
    public void EveryCallThatCanOpenADialogIsWatched(string description)
    {
        var (desktop, clock) = StillDesktop();
        using var installed = desktop.Install();
        var started = clock.GetTimestamp();

        ModalAwareInvoker.Execute(
            ProcessId,
            description,
            () => { },
            new PendingInvokeTracker(),
            windowEnumerator: _ => [Window(1, "Main")]
        );

        Assert.Equal(ModalAwareInvoker.LateModalGrace, clock.GetElapsedTime(started));
    }

    [Fact]
    public void ACancellationDuringTheWatchEndsItWithoutFailingTheCompletedCall()
    {
        var (desktop, clock) = StillDesktop();
        using var installed = desktop.Install();
        var operation = new OperationContext();
        OperationContext.Current.Value = operation;
        try
        {
            var started = clock.GetTimestamp();
            var reads = 0;

            var result = ModalAwareInvoker.Execute(
                ProcessId,
                "Invoke on 'Save'",
                () => { },
                new PendingInvokeTracker(),
                windowEnumerator: _ =>
                {
                    // The second read is the first poll of the watch; the client cancels right after it.
                    if (++reads == 2)
                    {
                        operation.Stop.Cancel();
                    }

                    return [Window(1, "Main")];
                }
            );

            Assert.Equal(PatternCallOutcome.Completed, result.Outcome);
            Assert.Null(result.ModalTitle);
            Assert.Equal(2, reads);
            Assert.Equal(TimeSpan.FromMilliseconds(100), clock.GetElapsedTime(started));
        }
        finally
        {
            OperationContext.Current.Value = null;
        }
    }

    [Fact]
    public void AnUnknownProcessIsNotWatched()
    {
        var (desktop, clock) = StillDesktop();
        using var installed = desktop.Install();
        var started = clock.GetTimestamp();
        var reads = 0;

        var result = ModalAwareInvoker.Execute(
            0,
            "Toggle on 'F12'",
            () => { },
            new PendingInvokeTracker(),
            windowEnumerator: _ =>
            {
                reads++;
                return [];
            }
        );

        Assert.Equal(PatternCallOutcome.Completed, result.Outcome);
        Assert.Null(result.ModalTitle);
        Assert.Equal(0, reads);
        Assert.Equal(TimeSpan.Zero, clock.GetElapsedTime(started));
    }

    private static PatternCallResult Completed(string? modal) => new(PatternCallOutcome.Completed, modal);

    [Fact]
    public void AVerifiedStateReadBeforeALateDialogIsUnsettled()
    {
        var reply = ClickTool.CheckedPatternResult(
            Completed("Save As"),
            new PatternVerdict("Toggled Pane to On", "verified", "On"),
            "Toggle",
            "not Off"
        );
        var result = ToolRegistry.WithDispatch(reply, 1);

        Assert.Equal("unsettled", result.Outcome!.Proof!.Verdict);
        Assert.Equal("On", result.Outcome.Proof.Observed);
        Assert.Equal("unverified", result.Outcome.Status);
        Assert.Equal("Save As", result.Outcome.ModalTitle);
        Assert.True(result.IsError != true);
        Assert.Equal(
            "Toggled Pane to On; then dialog \"Save As\" opened. Find it with windows_list_windows and handle it "
                + "before relying on the state read back.",
            result.Content[0].Text
        );
    }

    [Fact]
    public void AStateReadBackByTheSameProviderRemainsUnverified()
    {
        var reply = ClickTool.CheckedPatternResult(
            Completed(null),
            new PatternVerdict("Toggled Pane to On", "verified", "On"),
            "Toggle",
            "not Off"
        );
        var result = ToolRegistry.WithDispatch(reply, 1);

        Assert.Equal("self-reported", result.Outcome!.Proof!.Verdict);
        Assert.Equal("unverified", result.Outcome.Status);
        Assert.Contains("not independently verified", result.Content[0].Text);
    }

    [Fact]
    public void AMismatchBeforeALateDialogStaysAFailure()
    {
        var reply = ClickTool.CheckedPatternResult(
            Completed("Save As"),
            new PatternVerdict("Sent toggle to Pane, but it is still Off", "mismatch", "Off"),
            "Toggle",
            "not Off"
        );
        var result = ToolRegistry.WithDispatch(reply, 1);

        Assert.Equal("mismatch", result.Outcome!.Proof!.Verdict);
        Assert.Equal("failed", result.Outcome.Status);
        Assert.True(result.IsError);
    }

    [Fact]
    public void ACompletedCallWithoutAVerdictStillNamesTheLateDialog()
    {
        var reply = ClickTool.CheckedPatternResult(Completed("Save As"), "Invoked Save", null);

        Assert.Equal("Save As", reply.Outcome!.ModalTitle);
        Assert.StartsWith("Invoked Save; then dialog \"Save As\" opened.", reply.Content[0].Text);
    }
}
