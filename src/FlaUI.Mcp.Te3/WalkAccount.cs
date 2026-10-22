using System.Text.Json;
using FlaUI.Mcp;
using FlaUI.Mcp.Core;

/// <summary>
/// The account of an object navigation for its reply (Te3Page.WalkReport): the strategy, the type (the one TE3
/// confirmed, else the one asked for, so a miss still says what it looked for), the rows read, the measured indent,
/// the selects sent and, after the keyboard fallback, the rows walked.
/// </summary>
internal static class WalkAccount
{
    public static object Rows(SelectResult picked, WalkRequest request) =>
        new
        {
            strategy = picked.ByTable ? "rows-by-table" : "rows",
            objectType = TypeOf(picked, request),
            rowsRead = picked.RowsRead,
            indentPx = picked.IndentPx,
            selects = picked.Tried,
            truncated = picked.Truncated,
            pages = picked.Pages,
            truncatedByViewport = picked.TruncatedByViewport,
            rejected = picked.Rejected,
        };

    public static object Keys(SelectResult picked, WalkRequest request, WalkResult walk) =>
        new
        {
            strategy = "keys",
            objectType = TypeOf(picked, request),
            rowsRead = picked.RowsRead,
            indentPx = picked.IndentPx,
            selects = picked.Tried,
            truncated = picked.Truncated,
            pages = picked.Pages,
            truncatedByViewport = picked.TruncatedByViewport,
            rejected = picked.Rejected,
            rowsWalked = walk.RowsVisited,
        };

    /// <summary>
    /// The reply with the walk as Core's top-level structuredContent field <c>walk</c> (ToolOutcome.Details); a reply
    /// with no walk is returned unchanged.
    /// </summary>
    public static McpToolResult Attach(McpToolResult reply, object? walk)
    {
        if (walk == null)
        {
            return reply;
        }

        var outcome = reply.Outcome ?? new ToolOutcome("completed", "not-tracked");
        return reply with { Outcome = outcome with { Details = Details(walk, outcome.Details) } };
    }

    /// <summary><paramref name="existing"/> details with the walk added; null when there is neither.</summary>
    public static IReadOnlyDictionary<string, JsonElement>? Details(
        object? walk,
        IReadOnlyDictionary<string, JsonElement>? existing
    )
    {
        if (walk == null)
        {
            return existing;
        }

        return new Dictionary<string, JsonElement>(existing ?? new Dictionary<string, JsonElement>())
        {
            ["walk"] = JsonSerializer.SerializeToElement(walk, McpProtocol.JsonOptions),
        };
    }

    private static string? TypeOf(SelectResult picked, WalkRequest request) =>
        picked.ConfirmedType ?? (request.ObjectType.Length > 0 ? request.ObjectType : null);
}
