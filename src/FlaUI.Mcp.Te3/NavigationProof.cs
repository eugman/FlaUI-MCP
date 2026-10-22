using FlaUI.Mcp.Core;

/// <summary>
/// te3_navigate's proof (Core's ToolOutcome.proof). An object navigation compares the selection TE3 names after the
/// walk, from the status bar or Properties (name and type) and the table the walk reached, with the request; every
/// other destination is proven by its arrival check, an independent read that has already passed.
/// </summary>
internal static class NavigationProof
{
    private const string Selection = "TE3 status bar or Properties, read after the walk";

    /// <param name="type">The requested type; null or empty takes whatever type TE3 names.</param>
    /// <param name="reachedTable">The table the walk reached the object under.</param>
    public static Proof Object(string table, string name, string? type, SelectionView now, string? reachedTable)
    {
        var observedName = TomSelection.SelectionName(now.Bar) ?? now.Name;
        var observedType = TomSelection.SelectionType(now.Bar) ?? now.Type;
        var typed = !string.IsNullOrEmpty(type);
        var sameType =
            typed
            && (
                string.Equals(type, observedType, StringComparison.Ordinal)
                || TomSelection.ObjectTypeMatches(type!, observedType)
            );
        var expected = $"{name} ({(typed ? type : observedType)}) in {table}";
        if (observedName == null)
        {
            return new Proof(Proof.Unavailable, null, expected, Selection, true);
        }

        return Proof.Compare(
            expected,
            $"{observedName} ({(sameType ? type : observedType)}) in {reachedTable ?? "an unknown table"}",
            Selection,
            true
        );
    }

    /// <summary>
    /// The reply of a navigation whose arrival check passed, with its proof: the object's selection, else the check.
    /// </summary>
    public static async Task<FlaUI.Mcp.McpToolResult> Attach(
        FlaUI.Mcp.McpToolResult reply,
        Te3Page page,
        string destination,
        System.Text.Json.JsonElement a
    )
    {
        if (destination != "object")
        {
            return LayoutProof.Attach(reply, Arrived(destination));
        }

        string? type = a.TryGetProperty("objectType", out var requested) ? requested.GetString() : null;
        var proof = await page.ObjectProof(
            a.GetProperty("table").GetString()!,
            a.GetProperty("objectName").GetString()!,
            type
        );
        return LayoutProof.Attach(reply, proof);
    }

    /// <summary>A destination whose arrival check (a fresh read of TE3) has passed.</summary>
    public static Proof Arrived(string destination) =>
        Proof.Compare(destination, destination, "the destination's arrival check, read after navigating", true);

    /// <summary>The grant's state read back after te3_interaction_begin or _end; not an observation of TE3.</summary>
    public static Proof Grant(bool expectedActive, bool active) =>
        Proof.Compare(
            expectedActive ? "active" : "inactive",
            active ? "active" : "inactive",
            "the interaction grant's state",
            false
        );
}
