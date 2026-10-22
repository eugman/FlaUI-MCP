/// <summary>The filtered tree as one read gives it, with the object types Properties shows once a row is selected.</summary>
internal sealed class FakeRows(params (string? Value, int? Depth, string Type)[] rows) : IRowTree
{
    private TreeRow? selected;

    /// <summary>The row already selected before the walk (an earlier walk left it there).</summary>
    public int? InitiallySelected
    {
        init => selected = value is { } index ? All[index] : null;
    }

    /// <summary>When true, Properties' Name also lags behind the select for the stale reads.</summary>
    public bool PropertiesLate { get; init; }

    public bool Complete { get; init; } = true;

    public bool Measured { get; init; } = true;

    public bool Truncated { get; init; }

    /// <summary>The provider accepts Select and the selection stays where it was.</summary>
    public bool IgnoresSelect { get; init; }

    public List<int> Selects { get; } = [];

    private IReadOnlyList<TreeRow> All => [.. rows.Select((r, i) => new TreeRow(i, r.Value, r.Depth))];

    /// <summary>As live: the row read starts with Home, which moves the selection to the top row.</summary>
    public bool HomeMovesSelection { get; init; }

    /// <summary>Row reads, each of which sends Home (and PageDowns) live.</summary>
    public int Reads { get; private set; }

    /// <summary>Whether the in-place read (no keys) can measure the indent: the root and its child are in view.</summary>
    public bool InPlaceMeasured { get; init; } = true;

    public Task<RowRead> Read()
    {
        Reads++;
        if (HomeMovesSelection)
        {
            selected = All[0];
        }

        return Task.FromResult(new RowRead(Measured ? All : null, Complete, Measured ? 24 : null, Truncated));
    }

    // Unmeasured, the rows are read with no depth, as the live read gives them when the root is out of view.
    public Task<(IReadOnlyList<TreeRow>? Rows, int? Selected)> ReadInPlace() =>
        InPlaceMeasured
            ? ReadSelected()
            : Task.FromResult<(IReadOnlyList<TreeRow>?, int?)>(
                ([.. All.Select(r => r with { Depth = null })], selected?.Index)
            );

    public Task Select(TreeRow row)
    {
        Selects.Add(row.Index);
        selected = IgnoresSelect ? selected : row;
        sinceSelect = 0;
        return Task.CompletedTask;
    }

    /// <summary>Rows a read realizes around the selection; null realizes them all.</summary>
    public int? Viewport { get; init; }

    // As TE3 does: only the rows on screen around the selection, not the whole tree.
    public Task<(IReadOnlyList<TreeRow>? Rows, int? Selected)> ReadSelected() =>
        Task.FromResult<(IReadOnlyList<TreeRow>?, int?)>(
            (
                Viewport is { } size && selected != null
                    ? [.. All.Where(r => r.Index > selected.Index - size && r.Index <= selected.Index)]
                    : All,
                selected?.Index
            )
        );

    /// <summary>Reads after a select during which the status bar still names the previous selection.</summary>
    public int StaleBarReads { get; init; }

    /// <summary>When true, Properties shows no ObjectType (as for a column on 3.27.1).</summary>
    public bool PropertiesWithoutType { get; init; }

    /// <summary>Properties' Display Folder for whatever is selected; null when the row is not read.</summary>
    public string? Folder { get; init; }

    private string? bar;
    private int sinceSelect;

    public Task<SelectionView> View()
    {
        if (selected != null && sinceSelect++ >= StaleBarReads)
        {
            bar = $"{rows[selected.Index].Type} \"{selected.Value}\"";
        }

        var late = PropertiesLate && sinceSelect <= StaleBarReads;
        var type = selected == null || PropertiesWithoutType || late ? null : rows[selected.Index].Type;
        return Task.FromResult(
            new SelectionView(bar, late ? null : selected?.Value, type, selected == null ? null : Folder)
        );
    }

    public Task Wait(int ms) => Task.CompletedTask;
}
