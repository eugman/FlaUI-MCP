using System.Text.Json;
using FlaUI.Mcp.Core;

namespace FlaUI.Mcp;

/// <summary>Enforcing closed schemas: an undeclared argument is refused before the tool runs.</summary>
public partial class ToolRegistry
{
    // The declared arguments of each tool whose schema is closed (additionalProperties: false).
    private readonly Dictionary<string, string[]> _closedSchemas = new();

    public void RegisterTool(ITool tool)
    {
        _tools[tool.Name] = tool;
        _closedSchemas.Remove(tool.Name);
        if (DeclaredArguments(tool) is { } declared)
        {
            _closedSchemas[tool.Name] = declared;
        }
    }

    /// <summary>
    /// The declared arguments of a closed schema, or null for an open one. A definition that throws is left
    /// unenforced here; calling the tool reports its error, as before.
    /// </summary>
    private static string[]? DeclaredArguments(ITool tool)
    {
        JsonElement schema;
        try
        {
            schema = JsonSerializer.SerializeToElement(tool.GetDefinition().InputSchema, McpProtocol.JsonOptions);
        }
        catch (Exception)
        {
            return null;
        }

        if (
            schema.ValueKind == JsonValueKind.Object
            && schema.TryGetProperty("additionalProperties", out var additional)
            && additional.ValueKind == JsonValueKind.False
        )
        {
            return schema.TryGetProperty("properties", out var properties)
                ? properties.EnumerateObject().Select(p => p.Name).ToArray()
                : [];
        }

        return null;
    }

    /// <summary>The refusal for an argument a closed schema does not declare, or null when every one is declared.</summary>
    private McpToolResult? UndeclaredArgument(string name, JsonElement? arguments)
    {
        if (
            !_closedSchemas.TryGetValue(name, out var declared)
            || arguments is not { ValueKind: JsonValueKind.Object } args
            || args.EnumerateObject().Select(p => p.Name).FirstOrDefault(n => !declared.Contains(n)) is not { } unknown
        )
        {
            return null;
        }

        var known = declared.Length == 0 ? "none" : string.Join(", ", declared);
        return ErrorText($"Unknown argument '{unknown}' for {name}; no input sent. Declared arguments: {known}.") with
        {
            Outcome = new ToolOutcome("not-dispatched", "not-tracked") { Status = "failed", InputsSent = 0 },
        };
    }
}
