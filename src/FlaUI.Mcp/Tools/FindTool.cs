using System.Text.Json;
using PlaywrightWindows.Mcp.Core;

namespace PlaywrightWindows.Mcp.Tools;

public sealed class FindTool(ElementQuery query) : ToolBase
{
    internal static readonly JsonSerializerOptions SelectorOptions = new() { PropertyNameCaseInsensitive = true };

    public override string Name => "windows_find";
    public override string Description => "Find controls with exact UIA selectors and optional ancestor scope; returns compact properties, patterns and fresh refs. Omit selector for bounded raw-tree inspection. An absent state field means unknown. Does not invalidate snapshot refs.";
    internal static object SelectorSchema => new { type = "object", properties = new {
        automationId = new { type = "string" }, name = new { type = "string" },
        rootOnly = new { type = "boolean", description = "Match only the search root, not descendants." },
        controlType = new { type = "string", description = "UIA type, e.g. Button, Edit, Tree, MenuItem" },
        className = new { type = "string" }, value = new { type = "string" }, visible = new { type = "boolean" }, pattern = new { type = "string", @enum = new[] { "Value", "SelectionItem", "Toggle" } } } };
    public override object InputSchema => new { type = "object", properties = new {
        handle = new { type = "string" }, selector = SelectorSchema, within = SelectorSchema, includeOwned = new { type = "boolean", description = "Also search other visible native windows of this process, including popup menus (unscoped only)." },
        maxDepth = new { type = "integer" }, maxNodes = new { type = "integer", description = "Client-side traversal/result budget; cannot interrupt a blocking provider query." }, maxResults = new { type = "integer" }, ancestry = new { type = "boolean" }, includeBounds = new { type = "boolean", description = "Include physical screen bounds; omitted by default." }
    }, required = new[] { "handle" } };

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        try
        {
            var a = arguments ?? throw new ArgumentException("Arguments required");
            if (a.ValueKind != JsonValueKind.Object || !a.TryGetProperty("handle", out var handle) || handle.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(handle.GetString()))
                throw new ArgumentException("handle must be a window handle from windows_list_windows, not an element ref.");
            var selector = a.TryGetProperty("selector", out var s)
                ? s.Deserialize<ElementSelector>(SelectorOptions) ?? throw new ArgumentException("selector must be an object, not null.")
                : new();
            var within = a.TryGetProperty("within", out var w)
                ? w.Deserialize<ElementSelector>(SelectorOptions) ?? throw new ArgumentException("within must be an object, not null.")
                : null;
            int Limit(string key, int fallback) => a.TryGetProperty(key, out var v) ? v.GetInt32() : fallback;
            var found = query.Find(handle.GetString()!, selector, within, Limit("maxDepth", 24), Limit("maxNodes", 3000), Limit("maxResults", 30),
                GetBoolArgument(arguments, "includeOwned"), includeBounds: GetBoolArgument(arguments, "includeBounds"), describeMisses: true);
            object response = GetBoolArgument(arguments, "ancestry")
                ? new { result = found, ancestors = found.Elements.Select(e => new { e.Ref, chain = query.Ancestors(e.Ref) }) }
                : found;
            var json = JsonSerializer.SerializeToNode(response, McpProtocol.JsonOptions)!.AsObject();
            if (EmptyHint(selector, found) is { } hint) json["hint"] = hint;
            return Task.FromResult(TextResult(json.ToJsonString(McpProtocol.JsonOptions)));
        }
        catch (ProviderBlockedException ex) { return Task.FromResult(BlockedResult(ex.Pending)); }
        catch (Exception ex) when (ex is ArgumentException or JsonException or InvalidOperationException or FormatException)
        { return Task.FromResult(ErrorResult("windows_find: " + ex.Message)); }
    }

    internal static string? EmptyHint(ElementSelector selector, QueryResult found) =>
        found.Elements.Count == 0 && (selector.Name ?? selector.ControlType ?? selector.Value) != null
            ? "No match. Selectors are exact and case-sensitive. Item text is often in value rather than name, and a menu entry may be a Button; retry with fewer fields."
            : null;
}
