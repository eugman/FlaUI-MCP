/// <summary>
/// The row strategy's second try: when the object's own name shows no placed candidate (live, "Rate" and "Measure"
/// match calculation items in many tables and the filtered view gave no measurable indent), the tree is filtered by
/// the table's name instead, the table row is selected and opened, and the object is looked for under it.
/// </summary>
internal static partial class ObjectSelect
{
    public static async Task<SelectResult> ByTable(IRowTree tree, WalkRequest request, Func<bool> outOfTime)
    {
        await tree.Search(request.Table);
        var read = await tree.Read();
        var rows = read.Rows;
        var table = rows?.FirstOrDefault(r =>
            r.Value == request.Table && SearchPick.TableOf(rows, r.Index) == request.Table
        );
        if (table == null)
        {
            return new(
                false,
                null,
                rows?.Count ?? 0,
                read.IndentPx,
                0,
                $"Searching for the table's own name '{request.Table}' showed no row placed as that table.",
                true,
                read.Truncated
            );
        }

        if ((await tree.ReadSelected()).Selected != table.Index)
        {
            await tree.Select(table);
        }

        await tree.Expand();
        var found = await Run(tree, request, outOfTime);
        return found with { Tried = found.Tried + 1, ByTable = true };
    }
}
