using System.Drawing;
using FlaUI.Mcp.Core;
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
    public void TouchingPanesDragInsideTheUpperPaneNotTheLowerTitleBar()
    {
        // Seen live on 3.27.1 at 125%: TOM Explorer ends at y 651 and Properties starts there. Its title bar
        // starts at 651 and the splitter sits in TOM Explorer's last few rows; a drag from 651 undocked Properties.
        var tom = new Rectangle(1160, 91, 760, 590);
        var properties = new Rectangle(1160, 681, 760, 427);
        var (from, to) = SplitterPlan.Resize(properties, [tom], width: null, height: 320);
        Assert.Equal(681 - SplitterPlan.TouchingInset, from.Y);
        Assert.Equal(from.Y + 107, to.Y);
    }

    [Fact]
    public void TouchingPanesSideBySideDragInsideTheLeftPane()
    {
        // Seen live on 3.27.1: AI Assistant ends at x 1000 and the Expression Editor document starts there.
        var assistant = new Rectangle(0, 91, 1000, 1017);
        var editor = new Rectangle(1000, 91, 160, 1017);
        var (from, to) = SplitterPlan.Resize(assistant, [editor], width: 680, height: null);
        Assert.Equal(1000 - SplitterPlan.TouchingInset, from.X);
        Assert.Equal(from.X - 320, to.X);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void AOneOrTwoPixelGapIsTreatedAsTouching(int gap)
    {
        // The midpoint of a 1-2 px gap is the shared edge itself, where a drag grabs the lower title bar.
        var tom = new Rectangle(1160, 91, 760, 590);
        var properties = new Rectangle(1160, 681 + gap, 760, 427);
        var (from, _) = SplitterPlan.Resize(properties, [tom], width: null, height: 320);
        Assert.Equal(681 - SplitterPlan.TouchingInset, from.Y);
    }

    [Fact]
    public void ANewTitledWindowAfterTheDragMeansThePaneUndocked()
    {
        nint[] before = [0x10, 0x20];
        var after = new[]
        {
            new Win32WindowInfo(0x10, "Model - Tabular Editor 3", 7, true, false, false),
            new Win32WindowInfo(0x20, "", 7, true, true, false),
            new Win32WindowInfo(0x30, "Properties", 7, true, true, false),
        };

        var error = Assert.Throws<InvalidOperationException>(() =>
            SplitterPlan.ThrowIfUndocked(before, after, "Properties")
        );

        Assert.Contains("undocked: true", error.Message);
        Assert.Contains("'Properties'", error.Message);
        Assert.Contains("Next:", error.Message);
    }

    [Fact]
    public void AnUntitledPopupOrTheSameWindowsAreNotAnUndock()
    {
        nint[] before = [0x10];
        var after = new[]
        {
            new Win32WindowInfo(0x10, "Model - Tabular Editor 3", 7, true, false, false),
            new Win32WindowInfo(0x40, "", 7, true, true, false),
        };

        SplitterPlan.ThrowIfUndocked(before, after, "Properties");
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

    [Fact]
    public void WithNoDocumentPaneTheSplitterSitsBesideThePaneEdge()
    {
        // Seen live: all document tabs closed, so the right-hand column has no neighbour pane.
        var properties = new Rectangle(857, 558, 1063, 470);
        var window = new Rectangle(0, 43, 1920, 1030);
        var (from, to) = SplitterPlan.Resize(properties, [], width: 820, height: null, window: window);
        Assert.Equal(857 - SplitterPlan.SplitterOffset, from.X);
        Assert.Equal(from.X + 243, to.X);
    }

    [Fact]
    public void ARealNeighbourWinsOverTheFallback()
    {
        var window = new Rectangle(0, 0, 1920, 1030);
        var (from, _) = SplitterPlan.Resize(Explorer, [Documents], width: 900, height: null, window: window);
        Assert.Equal(827, from.X);
    }

    [Fact]
    public void ARealNeighbourBelowWinsOverTheFallbackAbove()
    {
        // TOM Explorer on top, Properties below it; the space above TOM Explorer is title bar and toolbars.
        var tom = new Rectangle(920, 85, 1000, 593);
        var properties = new Rectangle(920, 683, 1000, 345);
        var window = new Rectangle(0, 0, 1920, 1030);
        var (from, to) = SplitterPlan.Resize(tom, [properties], width: null, height: 600, window: window);
        Assert.Equal(680, from.Y);
        Assert.Equal(687, to.Y);
    }
}
