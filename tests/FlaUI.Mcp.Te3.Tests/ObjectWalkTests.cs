using Xunit;

/// <summary>
/// te3_navigate object's decisions over a scripted tree, as WalkToObject makes them: the walk, the one rescue of an
/// undecided stop, and the arrival check after the search is cleared. No desktop access.
/// </summary>
public sealed class ObjectWalkTests
{
    private static RowObservation At(int depth, string value, string? type) => new(value, type, depth);

    /// <summary>A tree whose selected row is read typed, as the live cursor types it from Properties.</summary>
    private sealed class TypedTree(FakeTree tree, string? propertiesType) : ITreeCursor
    {
        public async Task<RowObservation> Current()
        {
            var row = await tree.Current();
            return row.Type == null ? row with { Type = propertiesType } : row;
        }

        public Task<RowObservation?> Down() => tree.Down();

        public bool Stalled => tree.Stalled;
    }

    private static readonly WalkRequest InFolder = new("Brands", "Brand", "Column", "1. Brand Hierarchy");

    // What Properties shows for the selection: its Name and, when read, its Display Folder.
    private static Func<SelectionView> Shows(string? name, string? folder = null) =>
        () => new SelectionView(null, name, null, folder);

    [Fact]
    public async Task ASiblingFolderMissIsNeverTurnedIntoArrival()
    {
        // The rescue read only the selection: Brand in "2. Other" matched by name and type and the miss became Found.
        var tree = new TypedTree(
            new FakeTree(
                At(2, "Brands", "Table"),
                At(3, "1. Brand Hierarchy", "Folder"),
                At(4, "Sub Brand", "Column"),
                At(3, "2. Other", "Folder"),
                At(4, "Brand", "Column")
            ),
            "Column"
        );

        var walk = await ObjectWalk.Find(tree, InFolder, () => false, () => true, Shows("Brand"));

        Assert.False(walk.Found);
        Assert.False(walk.Rescuable);
    }

    [Fact]
    public async Task AClockStopOnTheUntypedObjectInTheFolderIsRescued()
    {
        // The status bar named another row, so the walk saw no type on Brand and the clock ran out there.
        var fake = new FakeTree(At(2, "Brands", "Table"), At(3, "1. Brand Hierarchy", "Folder"), At(4, "Brand", null));
        var downs = 0;

        var walk = await ObjectWalk.Find(
            new TypedTree(fake, "Column"),
            InFolder,
            () => ++downs > 2,
            () => true,
            Shows("Brand")
        );

        Assert.True(walk.Found, walk.Reason);
    }

    [Fact]
    public async Task AStopWhoseDepthIsUnknownIsNeverRescued()
    {
        var fake = new FakeTree(At(2, "Brands", "Table"), At(3, "1. Brand Hierarchy", "Folder"), new("Brand", null));
        var downs = 0;

        var walk = await ObjectWalk.Find(
            new TypedTree(fake, "Column"),
            InFolder,
            () => ++downs > 2,
            () => true,
            Shows("Brand")
        );

        Assert.False(walk.Found);
    }

    [Fact]
    public async Task NoRescueReadIsMadeWhenTheCallIsOutOfTime()
    {
        var fake = new FakeTree(At(2, "Brands", "Table"), At(3, "1. Brand Hierarchy", "Folder"), At(4, "Brand", null));
        var downs = 0;
        var reads = 0;

        var walk = await ObjectWalk.Find(
            new TypedTree(fake, "Column"),
            InFolder,
            () => ++downs > 2,
            () => false,
            () =>
            {
                reads++;
                return new SelectionView(null, "Brand", null, null);
            }
        );

        Assert.False(walk.Found);
        Assert.Equal(0, reads);
    }

    // Where the rows read after the clear place the selection: its table, and whether it is the object's row there.
    private static Func<Task<SelectionPlace>> At(string? table, bool placed = true) =>
        () => Task.FromResult(new SelectionPlace(table, placed));

    [Fact]
    public async Task TheArrivalCheckReadsTheRowTypedAsTheWalkTypesIt()
    {
        // After the search clears, the bar can name another row: the check must use Properties' type as the walk did.
        var found = new WalkResult(true, "Brands", 3, null);
        var untyped = new FakeTree(At(4, "Brand", null));

        Assert.True(
            await ObjectWalk.Arrived(new TypedTree(untyped, "Column"), found, InFolder, Shows("Brand"), At("Brands"))
        );
        Assert.False(
            await ObjectWalk.Arrived(new TypedTree(untyped, null), found, InFolder, Shows("Brand"), At("Brands"))
        );
        Assert.False(
            await ObjectWalk.Arrived(new TypedTree(untyped, "Column"), found, InFolder, Shows("Type"), At("Brands"))
        );
    }

    [Fact]
    public async Task ASelectionThatStillNamesTheObjectInItsTableAndFolderAfterTheClearHasArrived()
    {
        // Live (vte3g walk 1'): after the clear the rows re-indexed while the status bar still said Column "Brand".
        // The row index does not decide; the selection's identity and where the rows read now place it do.
        var found = new WalkResult(true, "Brands", 22, null);
        var cleared = new FakeTree(new RowObservation("Brand", "Column", null));

        Assert.True(await ObjectWalk.Arrived(cleared, found, InFolder, Shows("Brand"), At("Brands")));
        Assert.False(
            await ObjectWalk.Arrived(
                new FakeTree(new RowObservation("Brand", "Hierarchy", null)),
                found,
                InFolder,
                Shows("Brand"),
                At("Brands")
            )
        );
        Assert.False(
            await ObjectWalk.Arrived(
                new FakeTree(new RowObservation("Sub Brand", "Column", null)),
                found,
                InFolder,
                Shows(null),
                At("Brands")
            )
        );
    }

    [Fact]
    public async Task AnotherTablesSameNamedColumnNeverPassesForTheRequestedOne()
    {
        // Review: the check trusted walk.Table, so Sales[Brand] selected after the clear passed for Brands[Brand].
        // The table is read from the rows (the nearest table row above the selection under Tables), and the folder
        // check is made again; rows that cannot place the selection prove nothing.
        var found = new WalkResult(true, "Brands", 22, null);
        var brand = new FakeTree(new RowObservation("Brand", "Column", null));

        Assert.False(await ObjectWalk.Arrived(brand, found, InFolder, Shows("Brand"), At("Sales", placed: false)));
        Assert.False(await ObjectWalk.Arrived(brand, found, InFolder, Shows("Brand"), At("Brands", placed: false)));
    }

    [Fact]
    public async Task ASelectionWhoseTableRowIsOutOfViewIsConfirmedFromThePreClearPlaceAndProperties()
    {
        // Live (te3-eb2ed6a walks a-d): when the search clears TE3 scrolls the selected row to the top of the
        // viewport, so the table row is never in view. The status bar and Properties name the object, and the rows
        // read before the clear placed it under its table and in its folder: that placement stands unless Properties'
        // Display Folder, when it shows, is another folder, or the walk placed it under another table.
        var found = new WalkResult(true, "Brands", 22, null);
        var brand = new FakeTree(new RowObservation("Brand", "Column", null));
        var outOfView = At(null, placed: false);

        Assert.True(await ObjectWalk.Arrived(brand, found, InFolder, Shows("Brand", "1. Brand Hierarchy"), outOfView));
        Assert.True(await ObjectWalk.Arrived(brand, found, InFolder, Shows("Brand"), outOfView));
        Assert.False(await ObjectWalk.Arrived(brand, found, InFolder, Shows("Brand", "2. Other"), outOfView));
        Assert.False(
            await ObjectWalk.Arrived(brand, found with { Table = "Sales" }, InFolder, Shows("Brand"), outOfView)
        );
        Assert.False(await ObjectWalk.Arrived(brand, found, InFolder, Shows("Sub Brand"), outOfView));
        Assert.False(
            await ObjectWalk.Arrived(
                new FakeTree(new RowObservation("Brand", "Level", null)),
                found,
                InFolder,
                Shows("Brand"),
                outOfView
            )
        );
    }

    [Fact]
    public void AClearMissWithTheTableRowOutOfViewSaysWhatTe3NamesAndWhatWasPlaced()
    {
        var text = ObjectWalk.ClearMiss(
            new("Brand", "Column", null),
            new SelectionPlace(null, false),
            InFolder,
            new SelectionView(null, "Brand", null, "2. Other")
        );

        Assert.Contains("'Brand' (Column) in display folder '2. Other'", text);
        Assert.Contains("'Brand' (Column) in folder '1. Brand Hierarchy' under table 'Brands'", text);
        Assert.Contains("table row is not in view", text);
    }

    [Fact]
    public async Task AWalkThatWasNotFoundNeverArrives() =>
        Assert.False(
            await ObjectWalk.Arrived(
                new TypedTree(new FakeTree(At(4, "Brand", "Column")), "Column"),
                new WalkResult(false, "Brands", 3, "outside the folder"),
                InFolder,
                Shows("Brand"),
                At("Brands")
            )
        );

    [Theory]
    [InlineData("Brand", true)]
    [InlineData("Brands", true)]
    [InlineData("1. Brand Hierarchy", true)]
    [InlineData("Sub Brand", false)]
    [InlineData("Products", false)]
    [InlineData(null, false)]
    public void PropertiesAreReadOnlyForRowsTheWalkIsLookingFor(string? value, bool read) =>
        // Live, a Properties read on every settled row made each step 3.5-4 s and lost to the 18 s clock.
        Assert.Equal(read, ObjectWalk.Interesting(InFolder)(value));
}
