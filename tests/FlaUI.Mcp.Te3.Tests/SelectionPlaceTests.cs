using Xunit;

/// <summary>
/// Where the rows read after the search clears place the selection (re-review of 8e76100): the table is the nearest
/// table row above it under Tables, a row of unknown depth breaks that the way it breaks a candidate, and a table
/// row scrolled out of view leaves the table unknown, with a message that says so. Scripted rows.
/// </summary>
public sealed class SelectionPlaceTests
{
    private static readonly WalkRequest BrandsBrand = new("Brands", "Brand", "Column", "1. Brand Hierarchy");

    private static TreeRow Row(int index, string value, int? depth) => new(index, value, depth);

    [Fact]
    public void ATableRowInViewPlacesTheSelectionInItsTableAndFolder()
    {
        TreeRow[] rows =
        [
            Row(10, "Tables", 1),
            Row(11, "Brands", 2),
            Row(12, "1. Brand Hierarchy", 3),
            Row(13, "Brand", 4),
        ];

        Assert.Equal(new SelectionPlace("Brands", true), SearchPick.Place(rows, 13, BrandsBrand));
    }

    [Fact]
    public void ATableRowScrolledOutOfViewLeavesTheTableUnknownAndPlacesNothing()
    {
        // The unfiltered tree scrolled the selection into view; Brands and its folder are above the realized rows.
        TreeRow[] rows = [Row(48, "Sub Brand", 4), Row(49, "Sub Brand Name", 4), Row(50, "Brand", 4)];

        var place = SearchPick.Place(rows, 50, BrandsBrand);

        Assert.Null(place.Table);
        Assert.False(place.Placed);
        Assert.Contains(
            "table row is not in view",
            ObjectWalk.ClearMiss(new("Brand", "Column", 4), place, BrandsBrand, new(null, null, null, null))
        );
    }

    [Fact]
    public void ARowOfUnknownDepthBetweenTheTableAndTheSelectionBreaksTheTable()
    {
        TreeRow[] rows =
        [
            Row(10, "Tables", 1),
            Row(11, "Brands", 2),
            Row(12, "Sales", null),
            Row(13, "1. Brand Hierarchy", 3),
            Row(14, "Brand", 4),
        ];

        Assert.Null(SearchPick.TableOf(rows, 14));
        Assert.Equal(new SelectionPlace(null, false), SearchPick.Place(rows, 14, BrandsBrand));
    }

    [Fact]
    public void AnotherTablesColumnIsNamedAsMovedThere()
    {
        var text = ObjectWalk.ClearMiss(
            new("Brand", "Column", 4),
            new SelectionPlace("Sales", false),
            BrandsBrand,
            new(null, null, null, null)
        );

        Assert.Contains("Selection moved when the search was cleared", text);
        Assert.Contains("under table 'Sales'", text);
    }
}
