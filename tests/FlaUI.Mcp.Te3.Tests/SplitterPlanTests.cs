using System.Drawing;
using Xunit;

public sealed class SplitterPlanTests
{
    // The layout seen at 175%: documents on the left, TOM Explorer on the right, a few pixels apart.
    private static readonly Rectangle Documents = new(0, 90, 822, 840);
    private static readonly Rectangle Explorer = new(832, 90, 1060, 840);

    [Fact]
    public void WideningARightPaneDragsItsLeftEdgeLeft()
    {
        var (from, to) = SplitterPlan.Resize(Explorer, [Documents], width: 1200, height: null);
        Assert.Equal(827, from.X);
        Assert.Equal(827 - 140, to.X);
        Assert.Equal(from.Y, to.Y);
    }

    [Fact]
    public void NarrowingALeftPaneDragsItsRightEdgeLeft()
    {
        var (from, to) = SplitterPlan.Resize(Documents, [Explorer], width: 700, height: null);
        Assert.Equal(827, from.X);
        Assert.Equal(827 - 122, to.X);
    }

    [Fact]
    public void HeightUsesTheHorizontalSplitter()
    {
        var top = new Rectangle(832, 90, 1060, 420);
        var bottom = new Rectangle(832, 516, 1060, 414);
        var (from, to) = SplitterPlan.Resize(bottom, [top], width: null, height: 600);
        Assert.Equal(513, from.Y);
        Assert.Equal(513 - 186, to.Y);
        Assert.Equal(from.X, to.X);
    }

    [Fact]
    public void ANeighbourBeyondTheGapIsNotASplitter() =>
        Assert.Throws<InvalidOperationException>(() =>
            SplitterPlan.Resize(
                Explorer,
                [new Rectangle(0, 90, 832 - SplitterPlan.MaxGap - 10, 840)],
                width: 900,
                height: null
            )
        );

    [Fact]
    public void ExactlyOneDimension() =>
        Assert.Throws<ArgumentException>(() => SplitterPlan.Resize(Explorer, [Documents], 900, 600));
}
