using System.Text.Json;

/// <summary>te3_navigate and te3_model_state argument checks, made before attaching to TE3 or sending input.</summary>
public static partial class Te3Destinations
{
    internal static void Validate(JsonElement a, string tool, string modelState)
    {
        if (a.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("Arguments must be an object.");
        }

        if (
            HasProcessId(a, out var pid)
            && (pid.ValueKind != JsonValueKind.Number || !pid.TryGetInt32(out var id) || id < 1)
        )
        {
            throw new ArgumentException("processId must be a positive integer.");
        }

        // A state read names no destination; requiring one would reject every valid call.
        if (tool == modelState)
        {
            return;
        }

        if (!a.TryGetProperty("destination", out var d) || Te3Destinations.Find(d.GetString()) is not { } destination)
        {
            throw new ArgumentException(
                $"Unsupported destination; use one of: {string.Join(", ", Te3Destinations.Ids)}."
            );
        }

        if (destination.Id == "object")
        {
            ValidateObject(a);
        }
    }

    private static void ValidateObject(JsonElement a)
    {
        foreach (var field in new[] { "table", "objectName" })
        {
            if (!a.TryGetProperty(field, out var value) || string.IsNullOrWhiteSpace(value.GetString()))
            {
                throw new ArgumentException("Object destination requires " + field);
            }
        }

        // Optional: without it the first same-named object under the table that TE3 confirms is taken (a level).
        if (
            a.TryGetProperty("objectType", out var type)
            && type.ValueKind != JsonValueKind.Null
            && !Te3Destinations.ObjectTypes.Contains(type.GetString())
        )
        {
            throw new ArgumentException("Unsupported objectType.");
        }

        var isTable =
            a.TryGetProperty("objectType", out var requested)
            && requested.ValueKind == JsonValueKind.String
            && requested.GetString() == "Table";
        if (isTable && a.TryGetProperty("folder", out var folder) && !string.IsNullOrWhiteSpace(folder.GetString()))
        {
            throw new ArgumentException("folder applies to columns and measures; a table is in no display folder.");
        }
    }

    internal static bool HasProcessId(JsonElement a, out JsonElement pid) =>
        a.TryGetProperty("processId", out pid) && pid.ValueKind != JsonValueKind.Null;

    /// <summary>The caller's processId, or the only running TE3 when none was given.</summary>
}
