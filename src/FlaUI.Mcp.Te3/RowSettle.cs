/// <summary>
/// Waits for the search-filtered tree to render: TE3 applies the filter a moment after the text is typed, and a read
/// before then sees no rows or a tree still changing.
/// </summary>
internal static class RowSettle
{
    /// <summary>
    /// Reads the row count until two reads agree on a non-empty tree, or <paramref name="budgetMs"/> passes; returns
    /// the last count (0 when the tree stayed empty).
    /// </summary>
    public static async Task<int> Wait(Func<Task<int>> count, Func<int, Task> delay, int budgetMs, int pollMs)
    {
        var last = await count();
        for (var waited = 0; waited < budgetMs; waited += pollMs)
        {
            await delay(pollMs);
            var now = await count();
            if (now > 0 && now == last)
            {
                return now;
            }

            last = now;
        }

        return last;
    }
}
