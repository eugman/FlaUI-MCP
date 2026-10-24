using System.Drawing;
using System.Runtime.CompilerServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// Two ways the hit test proved a press on the wrong row. An expanded tree item's bounds include its children's
/// rows, so a point on a child row hit-tested as a "descendant" of the parent; and a container's answer was taken
/// as proof without checking that the element's own bounds hold the point.
/// </summary>
public sealed class HitTestRowsTests
{
    // The parent folder row and its first child row, as a tree reports them: the parent spans both.
    private static readonly Rectangle Folder = new(433, 192, 435, 44);
    private static readonly Rectangle ChildRow = new(433, 214, 435, 22);
    private static readonly Point OnChild = new(435, 225);

    private static AutomationElement Element() =>
        (AutomationElement)RuntimeHelpers.GetUninitializedObject(typeof(AutomationElement));

    private static (FakeDesktop Desktop, AutomationElement Folder, AutomationElement Child) Tree(ControlType rowType)
    {
        var folder = Element();
        var child = Element();
        var desktop = new FakeDesktop { HitTest = _ => child };
        desktop.ControlTypes[folder] = ControlType.TreeItem;
        desktop.ControlTypes[child] = rowType;
        desktop.Parents[child] = folder;
        desktop.ElementBounds[folder] = Folder;
        desktop.ElementBounds[child] = ChildRow;
        desktop.Descriptions[child] = $"{rowType} '2. Quantity'";
        return (desktop, folder, child);
    }

    [Theory]
    [InlineData(ControlType.TreeItem)]
    [InlineData(ControlType.ListItem)]
    [InlineData(ControlType.DataItem)]
    public void ARowInsideTheElementTakesThePressInsteadOfProvingIt(ControlType rowType)
    {
        var (desktop, folder, _) = Tree(rowType);
        using var installed = desktop.Install();

        var error = Assert.Throws<InputRefusedException>(() => ClickPoint.Verify(OnChild, folder));

        Assert.Equal("click-point-covered", error.Refusal.Code);
        Assert.Equal(
            $"The point (435,225) is on {rowType} '2. Quantity', a row inside the element; no input sent. Scroll the "
                + "element into view, then observe again.",
            error.Message
        );
    }

    [Fact]
    public void ACellInsideAGridRowIsNotARowInsideIt()
    {
        // WinForms DataGridView and DevExpress grids through the MSAA proxy: the row and its cells are all DataItem.
        var row = Element();
        var cell = Element();
        var desktop = new FakeDesktop { HitTest = _ => cell };
        desktop.ControlTypes[row] = ControlType.DataItem;
        desktop.ControlTypes[cell] = ControlType.DataItem;
        desktop.Parents[cell] = row;
        using var installed = desktop.Install();

        Assert.Equal("descendant", ClickPoint.Verify(OnChild, row));
    }

    [Theory]
    [InlineData(ControlType.TreeItem)]
    [InlineData(ControlType.ListItem)]
    public void ADataItemInsideTheSameTreeRowProvesThePress(ControlType rowType)
    {
        // TE3 TOM Explorer: TreeItem Node5 and DataItem Name row 5 have the same vertical row.
        var row = Element();
        var name = Element();
        var desktop = new FakeDesktop { HitTest = _ => name };
        desktop.ControlTypes[row] = rowType;
        desktop.ControlTypes[name] = ControlType.DataItem;
        desktop.Parents[name] = row;
        desktop.ElementBounds[row] = new Rectangle(433, 214, 435, 22);
        desktop.ElementBounds[name] = new Rectangle(480, 214, 388, 22);
        using var installed = desktop.Install();

        Assert.Equal("descendant", ClickPoint.Verify(new Point(500, 225), row));
    }

    [Fact]
    public void AnOffsetDataItemInsideTheSameSizedTreeRowStillCovers()
    {
        // The point is inside both rows, but their vertical edges differ by more than the 2 px same-row tolerance.
        var row = Element();
        var child = Element();
        var desktop = new FakeDesktop { HitTest = _ => child };
        desktop.ControlTypes[row] = ControlType.TreeItem;
        desktop.ControlTypes[child] = ControlType.DataItem;
        desktop.Parents[child] = row;
        desktop.ElementBounds[row] = new Rectangle(433, 214, 435, 22);
        desktop.ElementBounds[child] = new Rectangle(480, 218, 388, 22);
        using var installed = desktop.Install();

        var refusal = Assert.Throws<InputRefusedException>(() => ClickPoint.Verify(new Point(500, 225), row));
        Assert.Equal("click-point-covered", refusal.Refusal.Code);
        Assert.Equal(0, desktop.Dispatched);
    }

    [Fact]
    public void ADataItemAtTheTopOfAnExpandedParentStillCovers()
    {
        var parent = Element();
        var child = Element();
        var desktop = new FakeDesktop { HitTest = _ => child };
        desktop.ControlTypes[parent] = ControlType.TreeItem;
        desktop.ControlTypes[child] = ControlType.DataItem;
        desktop.Parents[child] = parent;
        desktop.ElementBounds[parent] = new Rectangle(433, 214, 435, 44);
        desktop.ElementBounds[child] = new Rectangle(480, 214, 388, 22);
        using var installed = desktop.Install();

        var refusal = Assert.Throws<InputRefusedException>(() => ClickPoint.Verify(new Point(500, 225), parent));
        Assert.Equal("click-point-covered", refusal.Refusal.Code);
    }

    [Theory]
    [InlineData(ControlType.TreeItem)]
    [InlineData(ControlType.ListItem)]
    public void ARowInsideAGridRowStillCovers(ControlType rowType)
    {
        var row = Element();
        var inner = Element();
        var desktop = new FakeDesktop { HitTest = _ => inner };
        desktop.ControlTypes[row] = ControlType.DataItem;
        desktop.ControlTypes[inner] = rowType;
        desktop.Parents[inner] = row;
        using var installed = desktop.Install();

        Assert.Throws<InputRefusedException>(() => ClickPoint.Verify(OnChild, row));
    }

    [Fact]
    public void ADescendantThatIsNotARowStillProvesThePoint()
    {
        var (desktop, folder, _) = Tree(ControlType.Text);
        using var installed = desktop.Install();

        Assert.Equal("descendant", ClickPoint.Verify(OnChild, folder));
    }

    [Fact]
    public void AChildRowItselfIsTheTargetWhenItIsAskedFor()
    {
        var (desktop, _, child) = Tree(ControlType.TreeItem);
        using var installed = desktop.Install();

        Assert.Equal("target", ClickPoint.Verify(OnChild, child));
    }

    [Fact]
    public void AContainersAnswerProvesOnlyAPointInsideTheElementsBounds()
    {
        // The tree answers for the point; the row asked for lies six rows up, so the press would go elsewhere.
        var tree = Element();
        var row = Element();
        var desktop = new FakeDesktop { HitTest = _ => tree };
        desktop.ControlTypes[tree] = ControlType.Tree;
        desktop.ControlTypes[row] = ControlType.TreeItem;
        desktop.Parents[row] = tree;
        desktop.ElementBounds[row] = new Rectangle(433, 82, 435, 22);
        desktop.Descriptions[tree] = "Tree 'TOM Explorer'";
        using var installed = desktop.Install();

        var error = Assert.Throws<InputRefusedException>(() => ClickPoint.Verify(OnChild, row));

        Assert.Equal("click-point-covered", error.Refusal.Code);
        Assert.StartsWith(
            "The point (435,225) is outside the element's bounds, on Tree 'TOM Explorer'; no input sent.",
            error.Message
        );
    }

    [Fact]
    public void AContainersAnswerWithUnreadableElementBoundsIsRefused()
    {
        var tree = Element();
        var row = Element();
        var desktop = new FakeDesktop { HitTest = _ => tree };
        desktop.ControlTypes[tree] = ControlType.Tree;
        desktop.Parents[row] = tree;
        using var installed = desktop.Install();

        Assert.Throws<InputRefusedException>(() => ClickPoint.Verify(OnChild, row));
    }

    [Fact]
    public void AContainersAnswerInsideTheElementsBoundsStillPasses()
    {
        var tree = Element();
        var row = Element();
        var desktop = new FakeDesktop { HitTest = _ => tree };
        desktop.ControlTypes[tree] = ControlType.Tree;
        desktop.Parents[row] = tree;
        desktop.ElementBounds[row] = ChildRow;
        using var installed = desktop.Install();

        Assert.Equal("ancestor", ClickPoint.Verify(OnChild, row));
    }
}
