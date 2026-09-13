using System.Text.Json;
using PlaywrightWindows.Mcp.Core;

namespace PlaywrightWindows.Mcp.Tools;

public sealed class FindTool(ElementQuery query) : ToolBase
{
    public override string Name => "windows_find";
    public override string Description => "Find controls with exact UIA selectors and optional ancestor scope; returns compact properties, patterns and fresh refs. Omit selector for bounded raw-tree inspection. Does not invalidate snapshot refs.";
    private static object SelectorSchema => new { type = "object", properties = new {
        automationId = new { type = "string" }, name = new { type = "string" },
        rootOnly = new { type = "boolean", description = "Match only the search root, not descendants." },
        controlType = new { type = "string", description = "UIA type, e.g. Button, Edit, Tree, MenuItem" },
        className = new { type = "string" }, value = new { type = "string" }, visible = new { type = "boolean" }, pattern = new { type = "string", @enum = new[] { "Value", "SelectionItem" } } } };
    public override object InputSchema => new { type = "object", properties = new {
        handle = new { type = "string" }, selector = SelectorSchema, within = SelectorSchema, includeOwned = new { type = "boolean", description = "Also search other visible native windows of this process, including popup menus (unscoped only)." },
        maxDepth = new { type = "integer" }, maxNodes = new { type = "integer", description = "Client-side traversal/result budget; cannot interrupt a blocking provider query." }, maxResults = new { type = "integer" }, ancestry = new { type = "boolean" }
    }, required = new[] { "handle" } };
    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var a = arguments ?? throw new ArgumentException("Arguments required");
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var selector = a.TryGetProperty("selector", out var s) ? s.Deserialize<ElementSelector>(options)! : new();
        var within = a.TryGetProperty("within", out var w) ? w.Deserialize<ElementSelector>(options) : null;
        int Limit(string key, int fallback) => a.TryGetProperty(key, out var v) ? v.GetInt32() : fallback;
        var found = query.Find(
            a.GetProperty("handle").GetString()!, selector, within, Limit("maxDepth",24), Limit("maxNodes",3000), Limit("maxResults",30), GetBoolArgument(arguments,"includeOwned"));
        return Task.FromResult(TextResult(JsonSerializer.Serialize(GetBoolArgument(arguments,"ancestry")
            ? (object)new { result = found, ancestors = found.Elements.Select(e => new { e.Ref, chain = query.Ancestors(e.Ref) }) } : found,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })));
    }
}
