using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>
/// Reading a TOM Explorer row's depth and type from what UIA and the status bar show. Numbers are the live 3.27.1
/// readings of 2026-10-21 on SpaceParts (vte3b RESULTS findings 2, 10 and 11).
/// </summary>
public sealed class TomRowsTests
{
    private static int next;

    private static ElementInfo Item(
        string type,
        string name,
        string? value,
        double x,
        double y,
        bool? selected = null
    ) =>
        new($"w1e{Interlocked.Increment(ref next)}", name, "", type, "", true, false, value, [], 2)
        {
            Selected = selected,
            Bounds = new ElementBounds(x, y, 700, 22),
        };

    // The 125% scale measured live: model root label 48 px right of its TreeItem, 24 px more per level.
    private static readonly TomRows.Scale Live125 = new(48, 24);

    [Theory]
    // TreeItem x 1161 before the preset, 1445 after it.
    [InlineData(1161, 1209, 0)] // model root SpaceParts
    [InlineData(1161, 1233, 1)] // Tables, Data Sources
    [InlineData(1161, 1257, 2)] // table Brands
    [InlineData(1445, 1565, 3)] // display folder 1. Brand Hierarchy; table permission Brands under a role
    [InlineData(1445, 1589, 4)] // column Brand inside the folder
    [InlineData(1445, 1613, 5)] // hierarchy levels
    public void TheLabelsIndentGivesTheDepth(double tree, double label, int depth) =>
        Assert.Equal(depth, TomRows.Depth(tree, label, Live125));

    [Theory]
    [InlineData(1161, 1161)]
    [InlineData(1161, 1220)]
    public void AnOffsetOffTheIndentGridIsNoDepth(double tree, double label) =>
        Assert.Null(TomRows.Depth(tree, label, Live125));

    [Theory]
    // TreeItem x, model root label x, its first child's label x, a column-in-folder label x (depth 4).
    [InlineData(1161, 1209, 1233, 1305)] // 125%, measured live
    [InlineData(929, 967, 986, 1043)] // 100%: 38.4 and 19.2 px, rounded by UIA
    [InlineData(1393, 1451, 1480, 1566)] // 150%: 57.6 and 28.8 px
    public void TheIndentIsMeasuredFromTheRootAndItsFirstChildAtAnyScale(
        double tree,
        double root,
        double child,
        double column
    )
    {
        ElementInfo[] elements =
        [
            Item("TreeItem", "Node0", "SpaceParts", tree, 214),
            Item("TreeItem", "Node1", "Data Sources", tree, 236),
            Item("DataItem", "Name row 0", "SpaceParts", root, 214),
            Item("DataItem", "Name row 1", "Data Sources", child, 236),
        ];

        var scale = TomRows.Calibrate(elements);

        Assert.NotNull(scale);
        Assert.Equal(4, TomRows.Depth(tree, column, scale));
    }

    [Fact]
    public void WithoutTheRootInViewThereIsNoScaleAndNoDepth()
    {
        ElementInfo[] elements =
        [
            Item("TreeItem", "Node50", "Brand", 1445, 434, selected: true),
            Item("DataItem", "Name row 50", "Brand", 1589, 434),
        ];

        Assert.Null(TomRows.Calibrate(elements));
        Assert.Null(TomRows.Selected(elements, null).Depth);
        Assert.Null(TomRows.Depth(1445, 1589, null));
    }

    [Fact]
    public void TheSelectedTreeItemPairsWithItsDataItemByRowIndex()
    {
        ElementInfo[] elements =
        [
            Item("TreeItem", "Node49", "1. Brand Hierarchy", 1445, 412),
            Item("TreeItem", "Node50", "Brand", 1445, 434, selected: true),
            Item("DataItem", "Name row 49", "1. Brand Hierarchy", 1565, 412),
            Item("DataItem", "Name row 50", "Brand", 1589, 434),
        ];

        var (selected, depth) = TomRows.Selected(elements, Live125);

        Assert.Equal("Brand", selected?.Value);
        Assert.Equal(4, depth);
    }

    [Fact]
    public void WithoutItsDataItemTheSelectedRowHasNoDepth()
    {
        ElementInfo[] elements = [Item("TreeItem", "Node50", "Brand", 1445, 434, selected: true)];

        Assert.Null(TomRows.Selected(elements, Live125).Depth);
    }

    [Fact]
    public void TheStatusBarTypeCountsOnlyWhenItNamesTheSelectedRow()
    {
        Assert.Equal("Column", TomRows.Type("Brand", "Column \"Brand\"", () => "Folder"));
        // A selected folder left the slot on the previous row (finding 10): Properties says what it is.
        Assert.Equal("Folder", TomRows.Type("2. Brand Attributes", "Column \"Type\"", () => "Folder"));
        Assert.Null(TomRows.Type("2. Brand Attributes", "Column \"Type\"", () => null));
    }

    [Fact]
    public void TheSelectionSlotIsReadWhateverTheRestOfTheBarShows()
    {
        // The load slot blanked, so the bar mapped as unrecognized and the walk saw no type at all (finding 11).
        string[] slots =
        [
            "",
            "",
            "770 BP issues",
            "",
            "Ln 0\tCol 0\t\tINS",
            "Column \"Brand\"",
            "sp-demo-mcp@WORK-LAPTOP",
            "MCP Stopped",
        ];

        Assert.Equal("Column \"Brand\"", Te3StatusBar.SelectionSlot(slots));
        Assert.Null(Te3StatusBar.SelectionSlot(["Model loaded.", "(Nothing selected)", "(No connection)"]));
    }

    [Fact]
    public void TheSelectionIsTheTargetWhenPropertiesConfirmItsNameAndType()
    {
        // Live, Column "Brand" was selected under Brands but the walk saw no type and reported a miss (finding 11).
        var untyped = new RowObservation("Brand", null, 4);

        Assert.True(PathWalker.Arrived(untyped, "Brand", "Column", "Brands", "Brands", "Brand", "Column"));
        Assert.True(
            PathWalker.Arrived(untyped with { Type = "Column" }, null, null, "Brands", "Brands", "Brand", "Column")
        );
    }

    [Fact]
    public void PropertiesNamingAnotherObjectOrAnotherTableIsNoArrival()
    {
        var row = new RowObservation("Brand", "Column", 4);

        Assert.False(PathWalker.Arrived(row, "Type", "Column", "Brands", "Brands", "Brand", "Column"));
        Assert.False(PathWalker.Arrived(row, "Brand", "Column", "Products", "Brands", "Brand", "Column"));
        Assert.False(
            PathWalker.Arrived(row with { Type = null }, "Brand", "Folder", "Brands", "Brands", "Brand", "Column")
        );
    }

    [Fact]
    public void AGroupIsOpenOnlyWhenTheRowBelowItIsDeeper()
    {
        ElementInfo[] open =
        [
            Item("TreeItem", "Node4", "Roles", 1161, 302, selected: true),
            Item("TreeItem", "Node5", "Brand VPs", 1161, 324),
            Item("DataItem", "Name row 4", "Roles", 1233, 302),
            Item("DataItem", "Name row 5", "Brand VPs", 1257, 324),
        ];
        ElementInfo[] closed =
        [
            Item("TreeItem", "Node4", "Roles", 1161, 302, selected: true),
            Item("TreeItem", "Node5", "Tables", 1161, 324),
            Item("DataItem", "Name row 4", "Roles", 1233, 302),
            Item("DataItem", "Name row 5", "Tables", 1233, 324),
        ];

        Assert.True(TomRows.ChildBelow(open, Live125));
        Assert.False(TomRows.ChildBelow(closed, Live125));
        Assert.False(TomRows.ChildBelow(open, null));
    }

    [Fact]
    public void OneReadGivesEveryRealizedRowWithItsDepthInTreeOrder()
    {
        ElementInfo[] elements =
        [
            Item("TreeItem", "Node7", "Brand", 1445, 434),
            Item("TreeItem", "Node5", "Brands", 1445, 390),
            Item("TreeItem", "Node6", "1. Brand Hierarchy", 1445, 412),
            Item("DataItem", "Name row 5", "Brands", 1541, 390),
            Item("DataItem", "Name row 6", "1. Brand Hierarchy", 1565, 412),
            Item("DataItem", "Name row 7", "Brand", 1589, 434),
        ];

        var rows = TomRows.Rows(elements, Live125);

        Assert.Equal(
            [new TreeRow(5, "Brands", 2), new TreeRow(6, "1. Brand Hierarchy", 3), new TreeRow(7, "Brand", 4)],
            rows
        );
    }

    [Fact]
    public void AVisibleScrollBarMeansRowsMayBeUnrealized()
    {
        Assert.True(TomRows.Scrolls([Item("ScrollBar", "Vertical", "0", 1868, 214)]));
        Assert.False(TomRows.Scrolls([Item("TreeItem", "Node0", "SpaceParts", 1161, 214)]));
    }
}
