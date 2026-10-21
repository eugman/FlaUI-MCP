/// <summary>Every row of a filtered tree read so far, and whether the viewport may still hide more.</summary>
/// <param name="Complete">True when the tree fits, or paging reached rows already read (its end).</param>
/// <param name="TruncatedByViewport">Paging stopped (the page limit or the clock) while new rows were still coming.</param>
public sealed record PagedRows(IReadOnlyList<TreeRow> Rows, int Pages, bool Complete, bool TruncatedByViewport);

/// <summary>
/// Reads a tree that realizes only its on-screen rows: one read, then, while it scrolls, a PageDown and another read,
/// until a read adds no row (the end) or the page limit or clock stops it. Rows are kept by their NodeN index.
/// </summary>
internal static class RowPager
{
    public static async Task<PagedRows> ReadAll(
        Func<Task<(IReadOnlyList<TreeRow> Rows, bool Scrolls)>> read,
        Func<Task> pageDown,
        int maxPages,
        Func<bool> outOfTime
    )
    {
        var (first, scrolls) = await read();
        var rows = first.ToDictionary(r => r.Index);
        var pages = 0;
        var added = first.Count;
        while (scrolls && added > 0 && pages < maxPages && !outOfTime())
        {
            await pageDown();
            pages++;
            var (next, _) = await read();
            added = next.Count(r => rows.TryAdd(r.Index, r with { Page = pages }));
        }

        var complete = !scrolls || added == 0;
        return new([.. rows.Values.OrderBy(r => r.Index)], pages, complete, !complete);
    }
}
