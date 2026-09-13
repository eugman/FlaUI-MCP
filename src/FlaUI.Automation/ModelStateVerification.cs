using System.Security.Cryptography;
using System.Text.Json;

/// <summary>Full-field canonical JSON metadata comparison, preserving array order.
/// Not an exact byte comparison, processed-data check, or unsaved-UI-state assertion.</summary>
public static class ModelStateVerification
{

    public static string Hash(string bim)
    {
        using var document = JsonDocument.Parse(bim);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("model", out var model) || model.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Expected a BIM database containing model metadata");
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) Write(document.RootElement, writer);
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    private static void Write(JsonElement value, Utf8JsonWriter writer)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var properties = value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal).ToArray();
            if (properties.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length)
                throw new ArgumentException("Duplicate JSON property in model metadata");
            writer.WriteStartObject();
            foreach (var property in properties) { writer.WritePropertyName(property.Name); Write(property.Value, writer); }
            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in value.EnumerateArray()) Write(item, writer);
            writer.WriteEndArray();
        }
        else value.WriteTo(writer);
    }
}
