using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>The one deliberate loosening of the grant: a few pixels of physical mouse drift are not the user.</summary>
public sealed class MouseDriftFilterTests
{
    private const int Move = 0x0200;

    // An injected move sets the reference point; physical moves start after the injected quiet period.
    private static MouseDriftFilter Placed(int x = 100, int y = 100)
    {
        var filter = new MouseDriftFilter();
        Assert.False(filter.Counts(false, Move, physical: false, x, y, now: 0));
        return filter;
    }

    private static bool Physical(MouseDriftFilter filter, int message, int x, int y, long now) =>
        filter.Counts(false, message, physical: true, x, y, now);

    [Fact]
    public void ANudgeOfManySmallMovesNearTheAnchorIsTolerated()
    {
        // A real nudge: one hand motion reported as many 1 px events (a 4-move cap revoked this live, 2026-11-05).
        var filter = Placed();
        var points = new[] { 101, 102, 103, 102, 101, 100, 101, 102, 103, 102 };
        for (var i = 0; i < 20; i++)
        {
            Assert.False(Physical(filter, Move, points[i % points.Length], 100 + (i % 3), 600 + (i * 10)));
        }

        Assert.Equal(20, filter.Tolerated);
    }

    [Fact]
    public void AMoveMoreThanTenPixelsFromTheAnchorCounts()
    {
        Assert.False(Physical(Placed(), Move, 110, 100, 600));
        Assert.True(Physical(Placed(), Move, 111, 100, 600));
        Assert.True(Physical(Placed(), Move, 108, 108, 600));
    }

    [Fact]
    public void SmallStepsCreepingPastTheRadiusCount()
    {
        var filter = Placed();
        Assert.False(Physical(filter, Move, 103, 100, 600));
        Assert.False(Physical(filter, Move, 106, 100, 650));
        Assert.False(Physical(filter, Move, 109, 100, 700));
        Assert.True(Physical(filter, Move, 112, 100, 750));
        Assert.Equal(3, filter.Tolerated);
    }

    [Fact]
    public void TheAnchorDoesNotFollowToleratedMoves()
    {
        // 116,100 is only 8 px from the last tolerated point but 16 px from the anchor.
        var filter = Placed();
        Assert.False(Physical(filter, Move, 108, 100, 600));
        Assert.True(Physical(filter, Move, 116, 100, 900));
    }

    [Fact]
    public void BackAndForthWithinTheRadiusStaysTolerated()
    {
        var filter = Placed();
        Assert.False(Physical(filter, Move, 108, 100, 600));
        Assert.False(Physical(filter, Move, 92, 100, 900));
        Assert.False(Physical(filter, Move, 106, 108, 1_200)); // exactly 10 px, diagonal
        Assert.Equal(3, filter.Tolerated);
    }

    [Fact]
    public void A999MsPauseDoesNotReanchor()
    {
        var filter = Placed();
        Assert.False(Physical(filter, Move, 108, 100, 600));
        Assert.True(Physical(filter, Move, 116, 100, 1_599));
    }

    [Fact]
    public void StopAndGoCreepGainsAtMostTheRadiusPerQuietSecond()
    {
        var filter = Placed();
        for (var cycle = 0; cycle < 5; cycle++)
        {
            Assert.False(Physical(filter, Move, 110 + (cycle * 10), 100, 600 + (cycle * 1_000)));
        }

        // One quiet second later, more than 10 px past the last rest point counts.
        Assert.True(Physical(filter, Move, 161, 100, 5_600));
    }

    [Fact]
    public void AnInjectedClickDropsTheAnchor()
    {
        var filter = Placed();
        Assert.False(Physical(filter, Move, 108, 100, 600));
        Assert.False(filter.Counts(false, 0x0201, physical: false, 108, 100, 700));

        // Re-anchored at 108,100 after the click, so 116,100 is 8 px away and tolerated.
        Assert.False(Physical(filter, Move, 116, 100, 1_300));
    }

    [Fact]
    public void AMoveCountedInTheInjectedWindowStillSetsTheAnchor()
    {
        var filter = Placed();
        Assert.True(Physical(filter, Move, 101, 100, 400));
        Assert.False(Physical(filter, Move, 105, 100, 700));
    }

    [Fact]
    public void AfterAQuietSecondANewNudgeAnchorsWhereThePointerRests()
    {
        var filter = Placed();
        Assert.False(Physical(filter, Move, 108, 100, 600));

        // Quiet for a second: the next drift is measured from 108,100, not from 100,100.
        Assert.False(Physical(filter, Move, 116, 100, 1_600));
        Assert.True(Physical(filter, Move, 119, 100, 1_700));
    }

    [Theory]
    [InlineData(0x0201)] // WM_LBUTTONDOWN
    [InlineData(0x0202)] // WM_LBUTTONUP
    [InlineData(0x0204)] // WM_RBUTTONDOWN
    [InlineData(0x0205)] // WM_RBUTTONUP
    [InlineData(0x020B)] // WM_XBUTTONDOWN
    [InlineData(0x020C)] // WM_XBUTTONUP
    [InlineData(0x020A)] // WM_MOUSEWHEEL
    [InlineData(0x020E)] // WM_MOUSEHWHEEL
    public void ButtonsAndWheelsAlwaysCount(int message)
    {
        var filter = Placed();

        Assert.True(Physical(filter, message, 100, 100, 600));
        Assert.Equal(0, filter.Tolerated);
    }

    [Fact]
    public void APhysicalKeyAlwaysCountsAndAnInjectedEventNever()
    {
        var filter = new MouseDriftFilter();

        Assert.True(filter.Counts(true, 0x0100, physical: true, 0, 0, 0));
        Assert.False(filter.Counts(true, 0x0100, physical: false, 0, 0, 0));
        Assert.False(filter.Counts(false, 0x0201, physical: false, 0, 0, 0));
    }

    [Fact]
    public void NothingIsToleratedWithin500MsOfAnInjectedMouseEvent()
    {
        var filter = Placed();
        Assert.True(Physical(filter, Move, 101, 100, 499));

        filter = Placed();
        Assert.False(filter.Counts(false, 0x0201, physical: false, 100, 100, 1_000));
        Assert.True(Physical(filter, Move, 101, 100, 1_400));
        Assert.False(Physical(Placed(), Move, 101, 100, 500));
    }

    [Fact]
    public void AMoveWithoutAReferencePointCounts() =>
        Assert.True(new MouseDriftFilter().Counts(false, Move, physical: true, 100, 100, 600));

    [Fact]
    public void InjectedMovesUpdateTheReferencePoint()
    {
        var filter = Placed();
        Assert.False(Physical(filter, Move, 101, 100, 600));
        Assert.False(filter.Counts(false, Move, physical: false, 500, 500, 700));

        // Measured from where the agent put the cursor, not from the last physical point.
        Assert.False(Physical(filter, Move, 501, 500, 1_300));
        Assert.True(Physical(Moved(), Move, 102, 100, 1_300));
    }

    private static MouseDriftFilter Moved()
    {
        var filter = Placed();
        Assert.False(Physical(filter, Move, 101, 100, 600));
        Assert.False(filter.Counts(false, Move, physical: false, 500, 500, 700));
        return filter;
    }
}
