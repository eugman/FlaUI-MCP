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
    public void UpToFourSmallMovesInASecondAreToleratedAndTheFifthCounts()
    {
        var filter = Placed();

        Assert.False(Physical(filter, Move, 101, 100, 600));
        Assert.False(Physical(filter, Move, 102, 100, 650));
        Assert.False(Physical(filter, Move, 103, 100, 700));
        Assert.False(Physical(filter, Move, 104, 100, 750));
        Assert.True(Physical(filter, Move, 105, 100, 800));

        Assert.Equal(4, filter.Tolerated);
    }

    [Fact]
    public void MovesTravellingMoreThanEightPixelsCount()
    {
        Assert.True(Physical(Placed(), Move, 109, 100, 600));

        var filter = Placed();
        Assert.False(Physical(filter, Move, 103, 100, 600));
        Assert.False(Physical(filter, Move, 106, 100, 650));
        Assert.True(Physical(filter, Move, 109, 100, 700));
        Assert.Equal(2, filter.Tolerated);
    }

    [Fact]
    public void TheWindowSlidesSoOldMovesExpire()
    {
        var filter = Placed();
        for (var i = 1; i <= 4; i++)
        {
            Assert.False(Physical(filter, Move, 100 + i, 100, 600 + i));
        }

        Assert.False(Physical(filter, Move, 105, 100, 1_700));
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
