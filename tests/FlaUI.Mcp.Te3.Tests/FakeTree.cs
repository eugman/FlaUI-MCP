/// <summary>A filtered tree as the keyboard sees it: one selected row at a time.</summary>
internal sealed class FakeTree(params RowObservation[] rows) : ITreeCursor
{
    private readonly HashSet<int> hidden = [];
    private int at;
    public int Downs { get; private set; }

    /// <summary>A slow tree: the selection does not move off this row in time, though rows are shown below it.</summary>
    public int? SlowAt { get; init; }

    public bool Stalled { get; private set; }

    /// <summary>
    /// As the live cursor reads it: only rows whose text is in this set keep their type (Properties is read only for
    /// the object, its table and the folder path); null keeps every type.
    /// </summary>
    public HashSet<string>? TypedOnly { get; init; }

    private RowObservation Seen(int index) =>
        TypedOnly == null || TypedOnly.Contains(rows[index].Value ?? "")
            ? rows[index]
            : rows[index] with
            {
                Type = null,
            };

    /// <summary>The rows collapsed, in order.</summary>
    public List<string> Collapsed { get; } = [];

    public Task<RowObservation> Current() => Task.FromResult(Seen(at));

    public Task<RowObservation?> Down()
    {
        Downs++;
        Stalled = at == SlowAt;
        var next = Enumerable.Range(at + 1, Math.Max(0, rows.Length - at - 1)).FirstOrDefault(i => !hidden.Contains(i));
        if (Stalled || next == 0)
        {
            return Task.FromResult<RowObservation?>(null);
        }

        at = next;
        return Task.FromResult<RowObservation?>(Seen(at));
    }

    // Left on an expanded row: the rows below it that are deeper are hidden, and the selection stays. Left on a row
    // with nothing open below it moves the selection to its parent, as TE3 does.
    public Task<bool> Collapse()
    {
        Collapsed.Add(rows[at].Value ?? "");
        var next = Enumerable.Range(at + 1, Math.Max(0, rows.Length - at - 1)).FirstOrDefault(i => !hidden.Contains(i));
        if (next == 0 || !(rows[next].Depth > rows[at].Depth))
        {
            at = Enumerable.Range(0, at).LastOrDefault(i => rows[i].Depth < rows[at].Depth);
            return Task.FromResult(false);
        }

        for (var i = at + 1; i < rows.Length && rows[i].Depth > rows[at].Depth; i++)
        {
            hidden.Add(i);
        }

        return Task.FromResult(true);
    }
}
