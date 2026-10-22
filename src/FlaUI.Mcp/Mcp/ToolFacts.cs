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

/// <summary>The values a string argument may take, written as the schema's enum.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ToolEnumAttribute(params string[] values) : Attribute
{
    public string[] Values { get; } = values;
}

/// <summary>An integer argument's minimum, and optionally its maximum and default.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ToolRangeAttribute(long minimum) : Attribute
{
    public long Minimum { get; } = minimum;

    /// <summary>The maximum; <see cref="long.MaxValue"/> leaves it out.</summary>
    public long Maximum { get; set; } = long.MaxValue;

    /// <summary>The default; <see cref="long.MinValue"/> leaves it out.</summary>
    public long Default { get; set; } = long.MinValue;
}

/// <summary>
/// A property whose schema the record cannot express (a shared selector, a type union, a key order of its own):
/// the public static member named here, on the record or the type that declares it, supplies it as written.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ToolSchemaFromAttribute(string member) : Attribute
{
    public string Member { get; } = member;
}

/// <summary>Write "required": [] even when every argument is optional, as a schema published that way did.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ToolSchemaRequiredListAttribute : Attribute;

/// <summary>The argument record of a tool that takes no arguments.</summary>
public sealed class NoArguments
{
    private NoArguments() { }
}

/// <summary>Builds a closed JSON schema from an argument record whose properties carry <see cref="ToolDocAttribute"/>.</summary>
public static class ToolSchema
{
    /// <summary>
    /// The schema for <typeparamref name="T"/>: one camelCase property per public property, in declaration order;
    /// required when it is a <c>required</c> member or its constructor parameter has no default (the list is left out when empty, unless the record
    /// carries <see cref="ToolSchemaRequiredListAttribute"/>); additionalProperties false.
    /// </summary>
    public static object For<T>()
    {
        var (properties, required) = Members(typeof(T), []);
        var schema = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = properties,
        };
        if (required.Count > 0 || typeof(T).GetCustomAttribute<ToolSchemaRequiredListAttribute>() != null)
        {
            schema["required"] = required;
        }

        return schema;
    }

    private static (Dictionary<string, object> Properties, List<string> Required) Members(Type record, Type[] outer)
    {
        if (outer.Contains(record))
        {
            throw new InvalidOperationException(
                $"Argument record {record.Name} refers to itself, so it has no finite schema; give that argument a "
                    + "[ToolSchemaFrom] schema instead."
            );
        }

        // Required: a `required` member, or a positional parameter with no default. Any other member is optional.
        var positional =
            record
                .GetConstructors()
                .OrderByDescending(c => c.GetParameters().Length)
                .FirstOrDefault()
                ?.GetParameters()
                .ToDictionary(p => p.Name!, p => p.HasDefaultValue, StringComparer.OrdinalIgnoreCase)
            ?? [];
        bool IsRequired(PropertyInfo property) =>
            property.IsDefined(typeof(System.Runtime.CompilerServices.RequiredMemberAttribute))
            || (positional.TryGetValue(property.Name, out var hasDefault) && !hasDefault);
        var properties = new Dictionary<string, object>();
        var required = new List<string>();
        foreach (var property in record.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.Name == "EqualityContract")
            {
                continue;
            }

            var name = char.ToLowerInvariant(property.Name[0]) + property.Name[1..];
            properties[name] = PropertySchema(property, record, [.. outer, record]);
            if (IsRequired(property))
            {
                required.Add(name);
            }
        }

        return (properties, required);
    }

    /// <summary>type, then enum, minimum, maximum, default, items, then description, then a nested object's members.</summary>
    private static object PropertySchema(PropertyInfo property, Type record, Type[] outer)
    {
        if (property.GetCustomAttribute<ToolSchemaFromAttribute>() is { } from)
        {
            return Supplied(from.Member, record);
        }

        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        var schema = new Dictionary<string, object> { ["type"] = JsonType(type) };
        if (property.GetCustomAttribute<ToolEnumAttribute>() is { } values)
        {
            schema["enum"] = values.Values;
        }

        if (property.GetCustomAttribute<ToolRangeAttribute>() is { } range)
        {
            AddRange(schema, range);
        }

        if (ElementType(type) is { } element)
        {
            schema["items"] = new Dictionary<string, object> { ["type"] = JsonType(element) };
        }

        if (property.GetCustomAttribute<ToolDocAttribute>() is { } doc)
        {
            schema["description"] = doc.Description;
        }

        if (IsRecord(type))
        {
            var (members, required) = Members(type, outer);
            schema["properties"] = members;
            if (required.Count > 0)
            {
                schema["required"] = required;
            }
        }

        return schema;
    }

    private static void AddRange(Dictionary<string, object> schema, ToolRangeAttribute range)
    {
        schema["minimum"] = range.Minimum;
        if (range.Maximum != long.MaxValue)
        {
            schema["maximum"] = range.Maximum;
        }

        if (range.Default != long.MinValue)
        {
            schema["default"] = range.Default;
        }
    }

    private static object Supplied(string member, Type record)
    {
        const BindingFlags Static = BindingFlags.Public | BindingFlags.Static;
        for (var type = record; type != null; type = type.DeclaringType)
        {
            if (type.GetProperty(member, Static) is { } property)
            {
                return property.GetValue(null)!;
            }

            if (type.GetField(member, Static) is { } field)
            {
                return field.GetValue(null)!;
            }
        }

        throw new InvalidOperationException(
            $"No static schema member {member} on {record.Name} or its declaring types."
        );
    }

    // Only a record (it has the compiler's public <Clone>$) is expanded into its members: List<T>'s Capacity or a
    // dictionary's Keys are not arguments, so other reference types are an untyped object or an array.
    private static bool IsRecord(Type type) => type.IsClass && type.GetMethod("<Clone>$") != null;

    /// <summary>The element type of an array or of a sequence such as List&lt;T&gt;, or null for anything else.</summary>
    private static Type? ElementType(Type type)
    {
        if (type.IsArray)
        {
            return type.GetElementType();
        }

        if (type == typeof(string) || !type.IsGenericType)
        {
            return null;
        }

        var sequence = type.GetInterfaces()
            .Append(type)
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        return sequence == null || typeof(System.Collections.IDictionary).IsAssignableFrom(type)
            ? null
            : sequence.GetGenericArguments()[0];
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

        if (ElementType(bare) != null)
        {
            return "array";
        }

        return bare == typeof(string) ? "string" : "object";
    }
}
