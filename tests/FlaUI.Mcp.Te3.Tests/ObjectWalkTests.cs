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

        var walk = await ObjectWalk.Find(tree, InFolder, () => false, () => true, () => "Brand");

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
            () => "Brand"
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
            () => "Brand"
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
                return "Brand";
            }
        );

        Assert.False(walk.Found);
        Assert.Equal(0, reads);
    }

    [Fact]
    public async Task TheArrivalCheckReadsTheRowTypedAsTheWalkTypesIt()
    {
        // After the search clears, the bar can name another row: the check must use Properties' type as the walk did.
        var found = new WalkResult(true, "Brands", 3, null);
        var untyped = new FakeTree(At(4, "Brand", null));

        Assert.True(await ObjectWalk.Arrived(new TypedTree(untyped, "Column"), found, InFolder, () => "Brand"));
        Assert.False(await ObjectWalk.Arrived(new TypedTree(untyped, null), found, InFolder, () => "Brand"));
        Assert.False(await ObjectWalk.Arrived(new TypedTree(untyped, "Column"), found, InFolder, () => "Type"));
    }

    [Fact]
    public async Task ASelectionThatStillNamesTheObjectAfterTheClearHasArrivedWhateverItsRowNow()
    {
        // Live (vte3g walk 1'): the filter cleared, the rows re-indexed and the depth could not be read from the
        // unfiltered view, while the status bar still said Column "Brand". The folder was checked during the walk;
        // after the clear the selection's identity (name, type, the table the walk confirmed) decides.
        var found = new WalkResult(true, "Brands", 22, null);
        var cleared = new FakeTree(new RowObservation("Brand", "Column", null));

        Assert.True(await ObjectWalk.Arrived(cleared, found, InFolder, () => "Brand"));
        Assert.False(
            await ObjectWalk.Arrived(
                new FakeTree(new RowObservation("Brand", "Hierarchy", null)),
                found,
                InFolder,
                () => "Brand"
            )
        );
        Assert.False(
            await ObjectWalk.Arrived(
                new FakeTree(new RowObservation("Sub Brand", "Column", null)),
                found,
                InFolder,
                () => null
            )
        );
        Assert.False(await ObjectWalk.Arrived(cleared, found with { Table = "Products" }, InFolder, () => "Brand"));
    }

    [Fact]
    public async Task AWalkThatWasNotFoundNeverArrives() =>
        Assert.False(
            await ObjectWalk.Arrived(
                new TypedTree(new FakeTree(At(4, "Brand", "Column")), "Column"),
                new WalkResult(false, "Brands", 3, "outside the folder"),
                InFolder,
                () => "Brand"
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
