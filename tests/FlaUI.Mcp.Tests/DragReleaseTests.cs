using System.Drawing;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Mcp;
using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// A drag's release lands on whatever is under the pointer then, not on what was proved before the button went
/// down. The live seed (TE3, 2026-10-22): the tree auto-scrolled six rows while the drag hovered its first row,
/// and the release moved a measure to another table's folder. The drop point is now checked again before the
/// release; a target that moved cancels the drag with Escape while the button is still down.
/// </summary>
[Collection(InputLeaseCollection.Name)]
public sealed partial class DragReleaseTests
{
    private static readonly Point From = new(10, 10);
    private static readonly Point To = new(50, 10);
    private static readonly Rectangle Row = new(433, 214, 435, 22);
    private static readonly Rectangle RowSixRowsDown = new(433, 346, 435, 22);

    private static AutomationElement Element() =>
        (AutomationElement)RuntimeHelpers.GetUninitializedObject(typeof(AutomationElement));

    private static JsonElement Args(object value) => JsonSerializer.SerializeToElement(value);

    private static (ElementRegistry Refs, SessionManager Sessions, string Handle) Window()
    {
        var sessions = new SessionManager(null, ProcessPolicy.AllowAll);
        var handle = sessions.PublishWindow(null, 0, FakeDesktop.TargetPid, FakeDesktop.Target);
        return (new ElementRegistry(), sessions, handle);
    }

    private static Task<McpToolResult> Run(MouseTool tool, object arguments)
    {
        var registry = new ToolRegistry();
        registry.RegisterTool(tool);
        return registry.ExecuteToolAsync(tool.Name, Args(arguments));
    }

    private static ClickGeometry Geometry(Rectangle bounds) => new(false, null, bounds, []);

    [Fact]
    public void ADropTargetThatMovedCancelsTheDragWithEscapeBeforeTheRelease()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        var checkedAt = new List<(Point Point, int Sent)>();

        var error = Assert.Throws<InputRefusedException>(() =>
            PointerActions.Drag(
                FakeDesktop.Target,
                () => (From, To),
                4,
                point =>
                {
                    checkedAt.Add((point, desktop.Dispatched));
                    return "the row is now elsewhere";
                }
            )
        );

        // The drop point is re-checked after the last move, with the button still down; then Escape, then the release.
        Assert.Equal([(To, 2 + 4)], checkedAt);
        Assert.Equal(2 + 4 + 2, desktop.Dispatched);
        Assert.Equal("drop-target-moved", error.Refusal.Code);
        Assert.True(error.Refusal.ActionDispatched);
        Assert.Equal(
            "The drop target moved during the drag: the row is now elsewhere. Escape was sent and the button was "
                + "released; whether the app dropped is not verified: observe the window, then retry.",
            error.Message
        );
    }

    [Fact]
    public void ADropTargetStillInPlaceReleasesWithoutAnEscape()
    {
        var desktop = new FakeDesktop { Pointer = new Point(1, 1) };
        using var installed = desktop.Install();

        var landed = PointerActions.Drag(FakeDesktop.Target, () => (From, To), 4, _ => null);

        Assert.Equal(2 + 4 + 1, desktop.Dispatched);
        Assert.Equal(To, landed);
    }

    [Fact]
    public void AWindowThatCoveredTheDropPointDuringTheDragCancelsIt()
    {
        // Checked for every drag, a raw `to` point included: another window may have come up over the drop point.
        var desktop = new FakeDesktop();
        desktop.WindowUnder = point =>
            point == To && desktop.Dispatched > 2 ? FakeDesktop.OtherHwnd : FakeDesktop.TargetHwnd;
        using var installed = desktop.Install();

        var error = Assert.Throws<InputRefusedException>(() =>
            PointerActions.Drag(FakeDesktop.Target, () => (From, To), 4)
        );

        Assert.Equal("drop-target-moved", error.Refusal.Code);
        Assert.Contains("the drop point (50,10) is no longer on the target window", error.Message);
        Assert.Equal(2 + 4 + 2, desktop.Dispatched);
    }

    [Fact]
    public void AFailedStepTakesTheSameWayOutWithoutAnEscapeToAnUnknownWindow()
    {
        // Focus lost mid-path: the step's refusal (target-changed) stands, Escape is not sent to whatever took the
        // foreground, the button is released, and the refusal says what went out.
        var desktop = new FakeDesktop { FocusLostAfter = 3 };
        using var installed = desktop.Install();

        var error = Assert.Throws<InputRefusedException>(() =>
            PointerActions.Drag(FakeDesktop.Target, () => (From, To), 4, _ => "never asked")
        );

        Assert.Equal("drag-interrupted", error.Refusal.Code);
        Assert.True(error.Refusal.ActionDispatched);
        Assert.Contains("no further input sent", error.Message);
        Assert.Contains("Escape was not sent (target-changed)", error.Message);
        Assert.EndsWith("whether the app dropped is not verified: observe the window, then retry.", error.Message);
        Assert.Equal(3 + 1, desktop.Dispatched);
    }

    [Fact]
    public void AnEscapeThatFailsStillReleasesTheButtonAndIsReported()
    {
        var desktop = new FakeDesktop();
        // Move to the start, press, four moves; the seventh input is the Escape.
        desktop.OnDispatch = count =>
        {
            if (count == 7)
            {
                throw new InvalidOperationException("keyboard hook");
            }
        };
        using var installed = desktop.Install();

        var error = Assert.Throws<InputRefusedException>(() =>
            PointerActions.Drag(FakeDesktop.Target, () => (From, To), 4, _ => "the row is now elsewhere")
        );

        Assert.Equal("drop-target-moved", error.Refusal.Code);
        Assert.Contains("Escape was not sent (keyboard hook) and the button was released;", error.Message);
        Assert.Equal(8, desktop.Dispatched);
    }

    [Fact]
    public void AReleaseThatFailsAfterTheEscapeIsReportedUnderTheMovedRefusal()
    {
        var desktop = new FakeDesktop();
        desktop.OnDispatch = count =>
        {
            if (count == 8)
            {
                throw new InvalidOperationException("input blocked");
            }
        };
        using var installed = desktop.Install();

        var error = Assert.Throws<InputRefusedException>(() =>
            PointerActions.Drag(FakeDesktop.Target, () => (From, To), 4, _ => "the row is now elsewhere")
        );

        Assert.Equal("drop-target-moved", error.Refusal.Code);
        Assert.Contains("Escape was sent and the release failed (input blocked);", error.Message);
        Assert.Equal(8, desktop.Dispatched);
    }

    [Fact]
    public void AProviderErrorWithTheButtonDownIsADragInterruptedRefusal()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();

        var error = Assert.Throws<InputRefusedException>(() =>
            PointerActions.Drag(FakeDesktop.Target, () => (From, To), 4, _ => throw new TimeoutException("provider"))
        );

        Assert.Equal("drag-interrupted", error.Refusal.Code);
        Assert.True(error.Refusal.ActionDispatched);
        Assert.Equal(
            "The drag failed while the button was down (provider). Escape was sent and the button was released; "
                + "whether the app dropped is not verified: observe the window, then retry.",
            error.Message
        );
        Assert.Equal(2 + 4 + 2, desktop.Dispatched);
    }

    [Fact]
    public void ACancellationWithTheButtonDownLetsGoThenRethrowsWithTheRefusalNoted()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        var operation = new OperationContext();
        OperationContext.Current.Value = operation;
        try
        {
            Assert.Throws<OperationCanceledException>(() =>
                PointerActions.Drag(
                    FakeDesktop.Target,
                    () => (From, To),
                    4,
                    _ => throw new OperationCanceledException()
                )
            );

            Assert.Equal(2 + 4 + 2, desktop.Dispatched);
            Assert.Equal("drag-interrupted", operation.Refusal!.Code);
            Assert.True(operation.Refusal.ActionDispatched);
            Assert.StartsWith(
                "The drag was cancelled while the button was down. Escape was sent",
                operation.Refusal.Message
            );
        }
        finally
        {
            OperationContext.Current.Value = null;
        }
    }

    [Fact]
    public void APointTheReReadBoundsNoLongerHoldNamesWhereTheElementIsNow()
    {
        var row = Element();
        var desktop = new FakeDesktop();
        desktop.Descriptions[row] = "TreeItem 'Net Orders (Quantity)'";
        using var installed = desktop.Install();

        Assert.Equal(
            "TreeItem 'Net Orders (Quantity)' is now at (433,346 435x22), which no longer holds the drop point (435,225)",
            ClickPoint.Moved(new Point(435, 225), Geometry(RowSixRowsDown), row)
        );
    }

    [Fact]
    public void APointStillOnTheElementAndStillHitTestedIsNotMoved()
    {
        var row = Element();
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();

        Assert.Null(ClickPoint.Moved(new Point(435, 225), Geometry(Row), row));
    }

    [Fact]
    public void APointNowCoveredGivesTheHitTestsReasonWithoutClaimingNothingWasSent()
    {
        var row = Element();
        var overlay = Element();
        var desktop = new FakeDesktop { HitTest = _ => overlay };
        desktop.Descriptions[overlay] = "Pane 'Insert mark'";
        using var installed = desktop.Install();
        var operation = new OperationContext();
        OperationContext.Current.Value = operation;
        try
        {
            Assert.Equal(
                "The point (435,225) is covered by Pane 'Insert mark'",
                ClickPoint.Moved(new Point(435, 225), Geometry(Row), row)
            );
            // The hit test's own refusal is not left on the operation: the drag's cancel is the refusal reported.
            Assert.Null(operation.Refusal);
        }
        finally
        {
            OperationContext.Current.Value = null;
        }
    }

    [Fact]
    public void AHitTestThatPassesIsNotCovering()
    {
        var row = Element();
        using var installed = new FakeDesktop().Install();

        Assert.Null(ClickHitTest.Covering(new Point(435, 225), row));
    }

    [Fact]
    public async Task ATreeThatScrolledUnderTheDragIsReportedAsAPartialFailureNamingTheRow()
    {
        // The live seed through the tool: the row is at Row when the drop point is chosen and six rows down by
        // the release. The fake moves it once the button is down.
        var row = Element();
        var folder = Element();
        var desktop = new FakeDesktop();
        desktop.Descriptions[row] = "TreeItem 'Net Orders (Quantity)'";
        desktop.Descriptions[folder] = "TreeItem '2. Quantity'";
        desktop.OnGeometry = _ => Geometry(desktop.Dispatched > 2 ? RowSixRowsDown : Row);
        // After Escape and the release, the folder that scrolled under the pointer is what a capture drag drops on.
        desktop.HitTest = _ => desktop.Dispatched >= 16 ? folder : row;
        using var installed = desktop.Install();
        var (refs, sessions, handle) = Window();
        using var _ = sessions;
        var reference = refs.Register(handle, row);

        var result = await Run(
            new MouseTool(refs, new PendingInvokeTracker(), sessions),
            new
            {
                action = "drag",
                @ref = reference,
                toRef = reference,
                toEdge = "left",
            }
        );

        // Not failed: every input went out, and only an OLE drag ends without a drop on Escape.
        Assert.True(result.IsError != true);
        Assert.Equal(
            "The drop target moved during the drag: TreeItem 'Net Orders (Quantity)' is now at (433,346 435x22), "
                + "which no longer holds the drop point (435,225). Escape was sent and the button was released; "
                + "whether the app dropped is not verified: observe the window, then retry. Under the pointer now: "
                + "TreeItem '2. Quantity'.",
            result.Content[0].Text
        );
        Assert.Equal("unverified", result.Outcome!.Status);
        Assert.Equal("not-established", result.Outcome.Verification);
        Assert.Equal("completed", result.Outcome.Dispatch);
        Assert.Equal("drop-target-moved", result.Outcome.Failure!.Code);
        Assert.True(result.Outcome.Failure.ActionDispatched);
        // Move to the start, press, twelve moves, Escape, release.
        Assert.Equal(16, result.Outcome.InputsSent);
        Assert.Equal(16, desktop.Dispatched);
    }

    [Fact]
    public async Task AnOverlayUnderTheDropPointAtReleaseCancelsTheDrag()
    {
        var row = Element();
        var mark = Element();
        var desktop = new FakeDesktop();
        desktop.ElementBounds[row] = Row;
        desktop.Descriptions[mark] = "Pane 'Insert mark'";
        desktop.HitTest = _ => desktop.Dispatched > 2 ? mark : row;
        using var installed = desktop.Install();
        var (refs, sessions, handle) = Window();
        using var _ = sessions;
        var reference = refs.Register(handle, row);

        var result = await Run(
            new MouseTool(refs, new PendingInvokeTracker(), sessions),
            new
            {
                action = "drag",
                @ref = reference,
                toRef = reference,
            }
        );

        Assert.Equal("drop-target-moved", result.Outcome!.Failure!.Code);
        Assert.Contains(
            "The point (650,225) is covered by Pane 'Insert mark'. Escape was sent",
            result.Content[0].Text
        );
        Assert.Equal(16, result.Outcome.InputsSent);
    }

    [Fact]
    public async Task AGeometryThatCannotBeReReadCountsAsMoved()
    {
        var row = Element();
        var desktop = new FakeDesktop();
        desktop.OnGeometry = _ =>
            desktop.Dispatched > 2 ? throw new InvalidOperationException("provider gone") : Geometry(Row);
        using var installed = desktop.Install();
        var (refs, sessions, handle) = Window();
        using var _ = sessions;
        var reference = refs.Register(handle, row);

        var result = await Run(
            new MouseTool(refs, new PendingInvokeTracker(), sessions),
            new
            {
                action = "drag",
                @ref = reference,
                toRef = reference,
            }
        );

        Assert.Equal("drop-target-moved", result.Outcome!.Failure!.Code);
        Assert.Contains("its geometry could not be re-read (provider gone). Escape was sent", result.Content[0].Text);
        Assert.Equal("unverified", result.Outcome.Status);
    }

    [Fact]
    public void ACancelledOperationIsNotTakenAsAMovedTarget()
    {
        var row = Element();
        var desktop = new FakeDesktop { OnGeometry = _ => throw new OperationCanceledException() };
        using var installed = desktop.Install();
        var refs = new ElementRegistry();
        var reference = refs.Register("w1", row);
        string? scrolled = null;
        var plan = new DragPlanner(new RefPoints(refs, new PendingInvokeTracker())).Plan(
            new DragRequest(reference, From, null, null, null, reference, "center", [], 4),
            "w1",
            ref scrolled
        );

        Assert.Throws<OperationCanceledException>(() => plan.DropMoved(To));
    }

    [Fact]
    public void ARawDropPointHasNothingToReRead()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        string? scrolled = null;
        var plan = new DragPlanner(new RefPoints(new ElementRegistry(), new PendingInvokeTracker())).Plan(
            new DragRequest(null, From, null, null, To, null, "center", [], 4),
            "w1",
            ref scrolled
        );

        Assert.Null(plan.DropMoved(To));
        Assert.Equal((From, To), plan.Resolve());
    }

    [Fact]
    public void AResizeDragsEndIsAPositionNotATarget()
    {
        var header = Element();
        var desktop = new FakeDesktop();
        desktop.ElementBounds[header] = Row;
        using var installed = desktop.Install();
        var refs = new ElementRegistry();
        var reference = refs.Register("w1", header);
        string? scrolled = null;
        var plan = new DragPlanner(new RefPoints(refs, new PendingInvokeTracker())).Plan(
            new DragRequest(reference, null, "right", 300, null, null, "center", [], 4),
            "w1",
            ref scrolled
        );

        var (from, to) = plan.Resolve();

        Assert.Equal(new Point(Row.Right - 1, 225), from);
        Assert.Equal(new Point(Row.Left + 300 - 1, 225), to);
        Assert.Null(plan.DropMoved(to));
    }
}
