using System.Reflection;
using System.Text;

namespace FlaUI.Mcp;

/// <summary>
/// Everything a client, a broker and the docs know about one tool, in one record: it generates the tools/list
/// entry (<see cref="Definition"/>) and the README table (<see cref="ReadmeTable"/>), so they cannot disagree.
/// </summary>
/// <param name="InputFree">Never sends input or changes the app (read-only).</param>
/// <param name="Observation">Reads the app's state, so a successful call is evidence of what is on screen.</param>
/// <param name="Recovery">Usable while an app's UI Automation provider is blocked (by handle, Win32 only).</param>
/// <param name="MutatingWhenArgument">Arguments that make an otherwise observing call change the desktop.</param>
public sealed record ToolFacts(
    string Name,
    string Description,
    McpToolAnnotations? Annotations,
    object Schema,
    bool InputFree,
    bool Observation,
    bool Recovery,
    IReadOnlyList<string> MutatingWhenArgument
)
{
    public McpTool Definition() =>
        new()
        {
            Name = Name,
            Description = Description,
            InputSchema = Schema,
            Annotations = Annotations,
        };

    /// <summary>The README's tool-annotation table, one row per tool, sorted by name.</summary>
    public static string ReadmeTable(IEnumerable<ToolFacts> tools)
    {
        static string Yes(bool? value) => value == true ? "yes" : "";
        var table = new StringBuilder();
        table.Append("| Tool | readOnly | destructive | idempotent | observation | recovery | mutating when |\n");
        table.Append("|---|---|---|---|---|---|---|\n");
        foreach (var tool in tools.OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            var when = string.Join(", ", tool.MutatingWhenArgument.Select(argument => $"`{argument}`"));
            var a = tool.Annotations;
            table.Append(
                $"| `{tool.Name}` | {Yes(a?.ReadOnlyHint)} | {Yes(a?.DestructiveHint)} | {Yes(a?.IdempotentHint)} | "
                    + $"{Yes(tool.Observation)} | {Yes(tool.Recovery)} | {when} |\n"
            );
        }

        return table.ToString();
    }
}

/// <summary>The description of one tool argument, on a property of an argument record.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ToolDocAttribute(string description) : Attribute
{
    public string Description { get; } = description;
}

/// <summary>Builds a closed JSON schema from an argument record whose properties carry <see cref="ToolDocAttribute"/>.</summary>
public static class ToolSchema
{
    /// <summary>
    /// The schema for <typeparamref name="T"/>: one camelCase property per public property, with its type and
    /// description; required when its constructor parameter has no default; additionalProperties false.
    /// </summary>
    public static object For<T>()
    {
        var optional = typeof(T)
            .GetConstructors()
            .OrderByDescending(c => c.GetParameters().Length)
            .First()
            .GetParameters()
            .Where(p => p.HasDefaultValue)
            .Select(p => p.Name!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var properties = new Dictionary<string, object>();
        var required = new List<string>();
        foreach (var property in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.Name == "EqualityContract")
            {
                continue;
            }

            var name = char.ToLowerInvariant(property.Name[0]) + property.Name[1..];
            properties[name] = new Dictionary<string, string>
            {
                ["type"] = JsonType(property.PropertyType),
                ["description"] = property.GetCustomAttribute<ToolDocAttribute>()?.Description ?? "",
            };
            if (!optional.Contains(property.Name))
            {
                required.Add(name);
            }
        }

        return new Dictionary<string, object>
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = properties,
            ["required"] = required,
        };
    }

    private static string JsonType(Type type)
    {
        var bare = Nullable.GetUnderlyingType(type) ?? type;
        if (bare == typeof(bool))
        {
            return "boolean";
        }

        if (bare == typeof(int) || bare == typeof(long))
        {
            return "integer";
        }

        return bare == typeof(string) ? "string" : "object";
    }
}
