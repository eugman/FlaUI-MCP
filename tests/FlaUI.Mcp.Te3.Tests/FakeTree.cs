/// <summary>A filtered tree as the keyboard sees it: one selected row at a time.</summary>
internal sealed class FakeTree(params RowObservation[] rows) : ITreeCursor
{
    private int at;
    public int Downs { get; private set; }

    /// <summary>A slow tree: the selection does not move off this row in time, though rows are shown below it.</summary>
    public int? SlowAt { get; init; }

    public bool Stalled { get; private set; }

    public Task<RowObservation> Current() => Task.FromResult(rows[at]);

    public Task<RowObservation?> Down()
    {
        Downs++;
        Stalled = at == SlowAt;
        if (Stalled || at + 1 >= rows.Length)
        {
            return Task.FromResult<RowObservation?>(null);
        }

        at++;
        return Task.FromResult<RowObservation?>(rows[at]);
    }
}
