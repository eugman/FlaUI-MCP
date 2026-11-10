using System.Drawing;
using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// The press and the release of a drag go out wherever the pointer is. A small physical nudge is tolerated without
/// revoking the grant, so one that arrives during the slow re-check before either button event would shift the
/// press or the drop by a row. The pointer is read again right before each and put back on the proved point.
/// </summary>
[Collection(InputLeaseCollection.Name)]
public sealed class DragPointerNudgeTests
{
    private static readonly Point From = new(10, 10);
    private static readonly Point To = new(50, 10);
    private static readonly Point Nudged = new(22, 25);

    /// <summary>Where the pointer was as each input went out.</summary>
    private static List<Point> Record(FakeDesktop desktop)
    {
        var sent = new List<Point>();
        desktop.OnDispatch = _ => sent.Add(desktop.Pointer);
        return sent;
    }

    [Fact]
    public void ANudgeDuringTheStartRecheckIsPutBackBeforeThePress()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        var sent = Record(desktop);

        var landed = PointerActions.Drag(
            FakeDesktop.Target,
            () => (From, To),
            4,
            beforeDown: () => desktop.Pointer = Nudged
        );

        // Move to the start, the move back, then the press: on the proved point, not the nudged one.
        Assert.Equal([From, From, From], sent.Take(3));
        Assert.Equal(1 + 1 + 1 + 4 + 1, desktop.Dispatched);
        Assert.Equal(To, landed);
    }

    [Fact]
    public void ANudgeDuringTheDropRecheckIsPutBackBeforeTheRelease()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        var sent = Record(desktop);

        var landed = PointerActions.Drag(
            FakeDesktop.Target,
            () => (From, To),
            4,
            _ =>
            {
                desktop.Pointer = Nudged;
                return null;
            }
        );

        // The move back and the release both go out on the drop point, with no Escape.
        Assert.Equal([To, To], sent.TakeLast(2));
        Assert.Equal(2 + 4 + 1 + 1, desktop.Dispatched);
        Assert.Equal(To, landed);
    }

    [Fact]
    public void APointerThatStayedPutCostsNoExtraMove()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();

        PointerActions.Drag(FakeDesktop.Target, () => (From, To), 4, _ => null, beforeDown: () => { });

        Assert.Equal(2 + 4 + 1, desktop.Dispatched);
    }

    [Fact]
    public void AStartThatCannotBeRestoredIsRefusedBeforeThePress()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        var nudged = false;
        desktop.PointerLandsOn = point => nudged ? Nudged : point;

        var error = Assert.Throws<InputRefusedException>(() =>
            PointerActions.Drag(
                FakeDesktop.Target,
                () => (From, To),
                4,
                beforeDown: () =>
                {
                    nudged = true;
                    desktop.Pointer = Nudged;
                }
            )
        );

        // Two pointer moves went out; the button never went down.
        Assert.Equal("pointer-moved", error.Refusal.Code);
        Assert.True(error.Refusal.ActionDispatched);
        Assert.Equal(2, desktop.Dispatched);
        Assert.Equal(
            "The pointer was moved off the start point (10,10) and could not be put back (it is at (22,25)); "
                + "the button was not pressed.",
            error.Message
        );
    }

    [Fact]
    public void ADropThatCannotBeRestoredCancelsTheDrag()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        var nudged = false;
        desktop.PointerLandsOn = point => nudged ? Nudged : point;

        var error = Assert.Throws<InputRefusedException>(() =>
            PointerActions.Drag(
                FakeDesktop.Target,
                () => (From, To),
                4,
                _ =>
                {
                    nudged = true;
                    desktop.Pointer = Nudged;
                    return null;
                }
            )
        );

        // The failed move back, then Escape and the release: the same way out as any failure with the button down.
        Assert.Equal("drag-interrupted", error.Refusal.Code);
        Assert.Contains("(pointer-moved)", error.Message);
        Assert.Contains("Escape was sent and the button was released", error.Message);
        Assert.Equal(2 + 4 + 1 + 2, desktop.Dispatched);
    }

    [Fact]
    public void ARestoreWithinTwoPixelsCounts()
    {
        // A move that lands a pixel off (mixed-DPI rounding) is the proved point for a row-sized target.
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        var nudged = false;
        desktop.PointerLandsOn = point => nudged ? new Point(point.X + 2, point.Y - 2) : point;

        PointerActions.Drag(
            FakeDesktop.Target,
            () => (From, To),
            4,
            beforeDown: () =>
            {
                nudged = true;
                desktop.Pointer = Nudged;
            }
        );

        Assert.True(desktop.Dispatched > 3);
    }
}
