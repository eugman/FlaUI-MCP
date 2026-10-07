using FlaUI.Mcp.Core;

/// <summary>
/// What the editor's status bar says about the loaded model. Read-only: this exists so an agent
/// learns in one call that a model cannot produce the screenshot it wants, instead of discovering
/// it dozens of calls later.
/// </summary>
/// <param name="Layout">
/// "recognized" when the bar matched the shape these fields were derived from, "unrecognized"
/// otherwise — in which case the named fields are null and only <paramref name="Slots"/> is
/// meaningful. Guessing at an unfamiliar bar would report the load state as the connection state.
/// </param>
/// <param name="LoadState">Load status, e.g. "Model loaded." or "Ready".</param>
/// <param name="BpaSummary">Best Practice Analyzer summary, e.g. "7257 BP issues".</param>
/// <param name="Caret">Caret position in the active editor, when one is open.</param>
/// <param name="Selection">Selected object, e.g. Measure "Gross Sales", or "(Nothing selected)".</param>
/// <param name="Connection">Connection state, e.g. "(No connection)" or "(No model loaded)".</param>
/// <param name="Slots">Every status-bar panel in order, so nothing observed is hidden by the mapping.</param>
public sealed record Te3ModelState(
    string Layout,
    string? LoadState,
    string? BpaSummary,
    string? Caret,
    string? Selection,
    string? Connection,
    IReadOnlyList<string> Slots
);

/// <summary>Maps status-bar panels onto named fields.</summary>
public static class Te3StatusBar
{
    /// <summary>
    /// The fewest text panels the mapping needs: load state, selection and connection. The caret
    /// panel is blank when no editor is open, which is a shape that was actually observed, so it
    /// cannot be required.
    /// </summary>
    private const int RecognizedTextPanels = 3;

    /// <summary>
    /// Most text panels the observed bar carries (message, caret, selection, connection). More than
    /// this means an add-in, a localised build or a state not observed here, where right-anchoring
    /// connection and selection would pick the wrong panels without any collision to catch it.
    /// </summary>
    private const int MaximumTextPanels = 4;

    /// <summary>
    /// Interpret the status bar's panels.
    /// Observed across three states — no model, a model loaded with nothing selected, and a measure
    /// selected — the bar keeps a stable order while every panel's text changes with state and none
    /// carries an automationId. So panels are identified by shape and position rather than by
    /// matching any particular string: matching "(No connection)" would simply miss on an empty
    /// instance, which reads "(No model loaded)" in the same slot.
    /// A bar that does not match that shape is reported as unrecognized rather than mapped on a
    /// guess, because a wrong field is worse than an absent one. "Recognized" means the panel count
    /// and the collision checks both held; it is not proof that TE3's layout is the one observed,
    /// since a build with an extra right-hand panel would shift connection and selection by one.
    /// </summary>
    /// <param name="panels">Direct children of the status bar, in order.</param>
    /// <returns>The interpreted state, including every raw slot.</returns>
    public static Te3ModelState Map(IReadOnlyList<ElementInfo> panels)
    {
        var slots = panels.Select(Text).ToArray();
        // The analyzer summary is the only Button on the bar; its label is the count itself.
        var bpa = panels.FirstOrDefault(p => p.ControlType == "Button");
        var texts = panels.Where(p => p.ControlType != "Button" && Text(p).Length > 0).ToArray();
        // Too few panels to map, or more than the observed layout has: an added panel shifts the
        // right-anchored slots silently, which is the mismapping this check exists to refuse.
        if (texts.Length < RecognizedTextPanels || texts.Length > MaximumTextPanels)
        {
            return new("unrecognized", null, null, null, null, null, slots);
        }

        var caret = texts.FirstOrDefault(p => Text(p).StartsWith("Ln", StringComparison.Ordinal));
        // Connection is the rightmost panel and selection the one before it; both are present once a
        // window exists, even reading "(No model loaded)" / "(Nothing selected)".
        var connection = texts[^1];
        var selection = texts[^2];
        var load = texts[0];
        // If those collide the bar is not the shape this mapping knows, so say so instead of
        // reporting one panel under two names.
        // load vs caret was the gap: TE3 blanks the message panel during some operations, leaving
        // [caret, selection, connection], so load became the caret panel and the bar still reported
        // "recognized" with LoadState reading "Ln 5  Col 38  INS".
        if (
            caret is not null
                && (
                    ReferenceEquals(caret, connection)
                    || ReferenceEquals(caret, selection)
                    || ReferenceEquals(caret, load)
                )
            || ReferenceEquals(load, selection)
            || ReferenceEquals(load, connection)
        )
        {
            return new("unrecognized", null, null, null, null, null, slots);
        }

        return new(
            "recognized",
            Text(load),
            bpa is null ? null : Text(bpa),
            caret is null ? null : Text(caret),
            Text(selection),
            Text(connection),
            slots
        );
    }

    private static string Text(ElementInfo panel) =>
        !string.IsNullOrEmpty(panel.Value) ? panel.Value
        : panel.Name is { Length: > 0 } and not "Static" ? panel.Name
        : string.Empty;
}
