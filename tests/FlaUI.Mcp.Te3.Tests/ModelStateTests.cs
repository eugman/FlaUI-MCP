using System.Text.Json;
using FlaUI.Mcp.Core;
using Xunit;

/// <summary>
/// The status bar keeps a stable order while every panel's text changes with state, and no panel
/// carries an automationId. Matching a particular string would miss: the slot reading
/// "(No connection)" with a model open reads "(No model loaded)" on an empty instance. All three
/// fixtures below were observed live on TE3 3.26.3.
/// </summary>
public sealed class ModelStateTests
{
    private static int next;

    private static ElementInfo Panel(string controlType, string name, string? value) =>
        new($"w1e{Interlocked.Increment(ref next)}", name, "", controlType, "", true, false, value, ["Invoke"], 1);

    private static ElementInfo Text(string? value) => Panel("Text", "Static", value);

    // Observed: model loaded, a measure selected, an editor open.
    private static ElementInfo[] MeasureSelected() =>
        [
            Text("Model loaded."),
            Text(null),
            Panel("Button", "7257 BP issues", null),
            Text(null),
            Text("Ln 5\tCol 38\t\tINS"),
            Text("Measure \"Gross Sales\""),
            Text("(No connection)"),
        ];

    // Observed: model loaded, nothing selected yet.
    private static ElementInfo[] NothingSelected() =>
        [
            Text("Model loaded."),
            Text(null),
            Panel("Button", "7257 BP issues", null),
            Text(null),
            Text("Ln 0\tCol 0\t\tINS"),
            Text("(Nothing selected)"),
            Text("(No connection)"),
        ];

    // Observed: a freshly launched instance with no model.
    private static ElementInfo[] EmptyInstance() =>
        [
            Text("Ready"),
            Text(null),
            Panel("Button", "Item", null),
            Text(null),
            Text(null),
            Text("(Nothing selected)"),
            Text("(No model loaded)"),
        ];

    [Fact]
    public void ALoadedModelWithASelectionIsReadCorrectly()
    {
        var state = Te3StatusBar.Map(MeasureSelected());
        Assert.Equal("recognized", state.Layout);
        Assert.Equal("Model loaded.", state.LoadState);
        Assert.Equal("7257 BP issues", state.BpaSummary);
        Assert.Equal("Measure \"Gross Sales\"", state.Selection);
        Assert.Equal("(No connection)", state.Connection);
        Assert.StartsWith("Ln 5", state.Caret);
    }

    [Fact]
    public void ALoadedModelWithNoSelectionIsReadCorrectly()
    {
        var state = Te3StatusBar.Map(NothingSelected());
        Assert.Equal("Model loaded.", state.LoadState);
        Assert.Equal("(Nothing selected)", state.Selection);
        Assert.Equal("(No connection)", state.Connection);
    }

    [Fact]
    public void AnEmptyInstanceIsReadCorrectly()
    {
        // The case that defeats string matching: the connection slot says "(No model loaded)" here.
        var state = Te3StatusBar.Map(EmptyInstance());
        Assert.Equal("Ready", state.LoadState);
        Assert.Equal("(No model loaded)", state.Connection);
        Assert.Equal("(Nothing selected)", state.Selection);
        Assert.Null(state.Caret);
    }

    [Fact]
    public void TheAnalyzerPanelIsFoundByBeingTheOnlyButton() =>
        // Its label is the count itself, so it reads "Item" when there is nothing to count.
        Assert.Equal("Item", Te3StatusBar.Map(EmptyInstance()).BpaSummary);

    [Fact]
    public void EveryRawSlotIsReportedSoNothingObservedIsHidden()
    {
        var state = Te3StatusBar.Map(MeasureSelected());
        Assert.Equal(7, state.Slots.Count);
        Assert.Contains("(No connection)", state.Slots);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void AnUnfamiliarBarIsReportedAsUnrecognizedRatherThanGuessed(int panels)
    {
        // A shorter bar than the mapping was derived from would otherwise report the load state as
        // the connection state — a confidently wrong field, which is worse than an absent one.
        var state = Te3StatusBar.Map([.. MeasureSelected().Where(p => !string.IsNullOrEmpty(p.Value)).Take(panels)]);
        Assert.Equal("unrecognized", state.Layout);
        Assert.Null(state.LoadState);
        Assert.Null(state.Connection);
        // The raw observation still comes back, so nothing is lost by refusing to interpret it.
        Assert.Equal(panels, state.Slots.Count);
    }

    [Fact]
    public void ABarWithNoCaretPanelIsStillRecognized()
    {
        // The empty instance has no editor open, so the caret slot is blank. That is an observed
        // shape, not an unfamiliar one, and must still map.
        var state = Te3StatusBar.Map(EmptyInstance());
        Assert.Equal("recognized", state.Layout);
        Assert.Null(state.Caret);
    }

    [Fact]
    public void CaretIsNotConfusedWithLoadState()
    {
        var state = Te3StatusBar.Map(MeasureSelected());
        Assert.NotEqual(state.Caret, state.LoadState);
    }

    [Fact]
    public void ModelStateSchemaTakesOnlyAProcessId()
    {
        var schema = JsonSerializer.Serialize(new Te3Companion(null!, Te3Companion.ModelState).InputSchema);
        Assert.Contains("processId", schema);
        Assert.DoesNotContain("destination", schema);
    }

    [Fact]
    public void ModelStateValidatesWithoutADestination() =>
        // te3_navigate requires one; requiring it here would reject every valid state read.
        Te3Companion.Validate(JsonSerializer.SerializeToElement(new { processId = 1234 }), Te3Companion.ModelState);

    [Fact]
    public void ModelStateStillRequiresARealProcessId() =>
        Assert.Throws<ArgumentException>(() =>
            Te3Companion.Validate(JsonSerializer.SerializeToElement(new { processId = 0 }), Te3Companion.ModelState)
        );

    [Fact]
    public void ModelStateDescriptionSaysItTakesNoFocus()
    {
        // te3_navigate's description says it takes focus; without this an operator would gate both
        // behind the same desktop handoff.
        var description = new Te3Companion(null!, Te3Companion.ModelState).Description;
        Assert.Contains("takes no desktop focus", description);
    }

    [Fact]
    public void ABlankedMessagePanelIsNotReadAsTheLoadState()
    {
        // TE3 blanks the message panel during some operations. That leaves
        // [caret, selection, connection], so the leftmost text became the load state and the bar
        // still reported "recognized" with LoadState reading "Ln 5	Col 38		INS".
        var state = Te3StatusBar.Map([
            Text(null),
            Text(null),
            Panel("Button", "7257 BP issues", null),
            Text(null),
            Text("Ln 5	Col 38		INS"),
            Text("Measure \"Gross Sales\""),
            Text("(No connection)"),
        ]);
        Assert.Equal("unrecognized", state.Layout);
        Assert.Null(state.LoadState);
    }

    [Fact]
    public void AnExtraRightHandPanelIsNotMappedOnAGuess()
    {
        // connection and selection are right-anchored, so one added panel — an add-in, a zoom or
        // progress panel, a localised build — shifts both silently with no collision to catch it.
        var state = Te3StatusBar.Map([
            Text("Model loaded."),
            Panel("Button", "7257 BP issues", null),
            Text("Ln 5	Col 38		INS"),
            Text("Measure \"Gross Sales\""),
            Text("(No connection)"),
            Text("100%"),
        ]);
        Assert.Equal("unrecognized", state.Layout);
        // The raw slots still come back, so a caller can see what was actually there.
        Assert.Contains("100%", state.Slots);
    }
}
