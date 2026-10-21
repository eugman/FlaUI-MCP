using Xunit;

/// <summary>
/// Reading a filtered tree longer than its viewport: TOM Explorer realizes only the ~23 rows on screen and treeList has
/// no Scroll pattern (te3-ea21625), so the reader pages down with keys and re-reads until the rows repeat.
/// </summary>
public sealed class RowPagerTests
{
    private static TreeRow Row(int index) => new(index, "Row " + index, 3);

    // A tree of `total` rows shown `visible` at a time; PageDown moves the window down by one page.
    private sealed class Viewport(int total, int visible)
    {
        private int top;

        public int Pages { get; private set; }

        public Task<(IReadOnlyList<TreeRow> Rows, bool Scrolls)> Read() =>
            Task.FromResult<(IReadOnlyList<TreeRow>, bool)>(
                ([.. Enumerable.Range(top, Math.Min(visible, total - top)).Select(Row)], total > visible)
            );

        public Task PageDown()
        {
            Pages++;
            top = Math.Min(top + visible, Math.Max(0, total - visible));
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ATreeThatFitsIsReadOnceWithNoKey()
    {
        var view = new Viewport(10, 23);

        var read = await RowPager.ReadAll(view.Read, view.PageDown, maxPages: 5, () => false);

        Assert.Equal(10, read.Rows.Count);
        Assert.Equal(0, view.Pages);
        Assert.True(read.Complete);
    }

    [Fact]
    public async Task ALongerTreeIsPagedUntilTheRowsRepeat()
    {
        var view = new Viewport(60, 23);

        var read = await RowPager.ReadAll(view.Read, view.PageDown, maxPages: 5, () => false);

        Assert.Equal(Enumerable.Range(0, 60), read.Rows.Select(r => r.Index));
        // Each row remembers the page it was in view on, so it can be brought back before it is selected.
        Assert.Equal(0, read.Rows[0].Page);
        Assert.Equal(1, read.Rows[30].Page);
        Assert.Equal(2, read.Rows[59].Page);
        Assert.True(read.Complete);
        Assert.False(read.TruncatedByViewport);
    }

    [Fact]
    public async Task APageLimitOrTheClockLeavesTheReadMarkedTruncatedByTheViewport()
    {
        var view = new Viewport(200, 23);

        var read = await RowPager.ReadAll(view.Read, view.PageDown, maxPages: 2, () => false);

        Assert.Equal(69, read.Rows.Count);
        Assert.False(read.Complete);
        Assert.True(read.TruncatedByViewport);
    }
}
