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
        // Seen live on 3.27.1 at 125%, in screen coordinates (651 in UIA-window-relative ones): TOM Explorer ends at
        // y 681 and Properties starts there. Its title bar starts at 681 and the splitter sits in TOM Explorer's last
        // few rows; a drag from 681 undocked Properties.
        var tom = new Rectangle(1160, 91, 760, 590);
        var properties = new Rectangle(1160, 681, 760, 427);
        var (from, to) = SplitterPlan.Resize(properties, [tom], width: null, height: 320, dpi: 120);
        Assert.Equal(681 - SplitterPlan.TouchingInset(120), from.Y);
        Assert.Equal(from.Y + 107, to.Y);
    }

    [Fact]
    public void TouchingPanesSideBySideDragInsideTheLeftPane()
    {
        // Seen live on 3.27.1: AI Assistant ends at x 1000 and the Expression Editor document starts there.
        var assistant = new Rectangle(0, 91, 1000, 1017);
        var editor = new Rectangle(1000, 91, 160, 1017);
        var (from, to) = SplitterPlan.Resize(assistant, [editor], width: 680, height: null, dpi: 120);
        Assert.Equal(1000 - SplitterPlan.TouchingInset(120), from.X);
        Assert.Equal(from.X - 320, to.X);
    }

    [Fact]
    public void ARightPaneTouchingADocumentUsesTheSharedEdge()
    {
        // Seen live on 3.27.1 at 125%, window restored at 0,0,1920,1080 (UIA origin 1,39), in screen coordinates: the
        // Expression Editor document ends at x 1159 and TOM Explorer starts there. A drag 3 px inside the document
        // (x 1156) left TOM Explorer at 760. A later live probe at 3 px inside TOM also missed; the shared edge worked.
        var editor = new Rectangle(681, 100, 478, 947);
        var tom = new Rectangle(1159, 100, 760, 549);
        var properties = new Rectangle(1159, 649, 760, 398);
        var (from, to) = SplitterPlan.Resize(
            tom,
            [editor, properties],
            width: 480,
            height: null,
            documents: [editor],
            dpi: 120
        );
        Assert.Equal(1159, from.X);
        Assert.Equal(from.X + 280, to.X);
        Assert.Equal("left", SplitterPlan.Edge(tom, from, width: true));
    }

    [Fact]
    public void ARightPaneTouchingADocumentUsesItsSharedEdgeOnAMixedDpiDisplay()
    {
        // Live TE3 3.27.1: monitor DPI 96, window DPI 120. The inset at x=-463 sent a drag but changed no width;
        // the shared edge at x=-466 resized TOM Explorer from 466 to 480 without floating it.
        var window = new Rectangle(-1920, 24, 1920, 1008);
        var editor = new Rectangle(-1240, 73, 774, 933);
        var tom = new Rectangle(-466, 73, 466, 619);
        var properties = new Rectangle(-466, 692, 466, 314);

        var (from, to) = SplitterPlan.Resize(
            tom,
            [editor, properties],
            width: 480,
            height: null,
            window: window,
            documents: [editor],
            dpi: 120
        );

        Assert.Equal(new Point(tom.Left, 382), from);
        Assert.Equal(new Point(tom.Left - 14, 382), to);
    }

    [Fact]
    public void ALeftPaneTouchingADocumentStillDragsInsideItself()
    {
        // The live AI Assistant step that reached its width: the dock pane is the first pane, so nothing changes.
        var assistant = new Rectangle(1, 100, 638, 947);
        var editor = new Rectangle(639, 100, 520, 947);
        var (from, _) = SplitterPlan.Resize(
            assistant,
            [editor],
            width: 680,
            height: null,
            documents: [editor],
            dpi: 120
        );
        Assert.Equal(639 - SplitterPlan.TouchingInset(120), from.X);
        Assert.Equal("right", SplitterPlan.Edge(assistant, from, width: true));
    }

    [Fact]
    public void ALowerPaneBelowAnotherPaneStillDragsInsideTheUpperOne()
    {
        var tom = new Rectangle(1159, 100, 760, 549);
        var properties = new Rectangle(1159, 649, 760, 398);
        var editor = new Rectangle(681, 100, 478, 947);
        var (from, _) = SplitterPlan.Resize(properties, [tom, editor], null, 320, documents: [editor], dpi: 120);
        Assert.Equal(649 - SplitterPlan.TouchingInset(120), from.Y);
        Assert.Equal("top", SplitterPlan.Edge(properties, from, width: false));
    }

    [Fact]
    public void AMissedDragSaysWhichEdgeFromWhereToWhereTheReachedSizeAndAttempts()
    {
        var message = SplitterPlan.Missed(
            "TOM Explorer",
            "left",
            new Point(1158, 335),
            new Point(1438, 335),
            480,
            760,
            1
        );

        Assert.Contains("'TOM Explorer'", message);
        Assert.Contains("left edge", message);
        Assert.Contains("(1158,335) to (1438,335)", message);
        Assert.Contains("requested 480", message);
        Assert.Contains("reached 760", message);
        Assert.Contains("1 attempt", message);
        Assert.Contains("Inspect before retrying", message);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void AOneOrTwoPixelGapIsTreatedAsTouching(int gap)
    {
        // The midpoint of a 1-2 px gap is the shared edge itself, where a drag grabs the lower title bar.
        var tom = new Rectangle(1160, 91, 760, 590);
        var properties = new Rectangle(1160, 681 + gap, 760, 427);
        var (from, _) = SplitterPlan.Resize(properties, [tom], width: null, height: 320, dpi: 120);
        Assert.Equal(681 - SplitterPlan.TouchingInset(120), from.Y);
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
        var (from, to) = SplitterPlan.Resize(properties, [], width: 820, height: null, window: window, dpi: 168);
        Assert.Equal(857 - SplitterPlan.SplitterOffset(168), from.X);
        Assert.Equal(from.X + 243, to.X);
    }

    [Theory]
    [InlineData(96u, 2, 3)]
    [InlineData(120u, 3, 4)]
    [InlineData(144u, 3, 5)]
    [InlineData(168u, 4, 5)]
    public void TheInsetsScaleWithTheWindowDpiAndTheGapMidpointDoesNot(uint dpi, int inset, int offset)
    {
        // The inset (3 px seen at 125%) and the splitter offset (5 px seen at 175%) are device-independent pixels
        // scaled by the window's DPI; the midpoint of a real gap needs no scale.
        Assert.Equal(inset, SplitterPlan.TouchingInset(dpi));
        Assert.Equal(offset, SplitterPlan.SplitterOffset(dpi));

        var tom = new Rectangle(1160, 91, 760, 590);
        var properties = new Rectangle(1160, 681, 760, 427);
        var (touching, _) = SplitterPlan.Resize(properties, [tom], null, 320, dpi: dpi);
        Assert.Equal(681 - inset, touching.Y);

        var (gap, _) = SplitterPlan.Resize(Explorer, [Documents], 1200, null, dpi: dpi);
        Assert.Equal(827, gap.X);

        var alone = new Rectangle(857, 558, 1063, 470);
        var (beside, _) = SplitterPlan.Resize(alone, [], 820, null, new Rectangle(0, 43, 1920, 1030), dpi: dpi);
        Assert.Equal(857 - offset, beside.X);
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
