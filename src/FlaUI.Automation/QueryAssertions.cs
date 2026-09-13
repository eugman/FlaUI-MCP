using System.Text.Json;

public sealed record QueryExpectation(string[] Columns, JsonElement[][] Rows);

/// <summary>Exact ordered result assertions. A rendered cell never proves completeness.</summary>
public static class QueryAssertions
{
    public static void VerifyScalar(string response, decimal expected)
    {
        using var document = JsonDocument.Parse(response);
        if (!document.RootElement.TryGetProperty("rows", out var rows) ||
            rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() != 1 ||
            rows[0].ValueKind != JsonValueKind.Object || rows[0].EnumerateObject().Count() != 1)
            throw new InvalidOperationException("Expected exactly one scalar query result");
        var column = rows[0].EnumerateObject().Single().Name;
        Verify(response, new([column], [[JsonSerializer.SerializeToElement(expected)]]));
    }

    public static void Validate(QueryExpectation expected)
    {
        if (expected.Columns is null || expected.Rows is null || expected.Columns.Length == 0 ||
            expected.Columns.Any(string.IsNullOrWhiteSpace) || expected.Columns.Distinct().Count() != expected.Columns.Length ||
            expected.Rows.Any(r => r is null || r.Length != expected.Columns.Length ||
                r.Any(v => v.ValueKind is not (JsonValueKind.Number or JsonValueKind.String or JsonValueKind.Null or JsonValueKind.True or JsonValueKind.False))))
            throw new ArgumentException("Expected result requires unique nonblank columns and rectangular scalar rows");
    }
    public static void Verify(string response, QueryExpectation expected)
    {
        Validate(expected);
        using var document = JsonDocument.Parse(response);
        var root = document.RootElement;
        if (!root.TryGetProperty("truncated", out var truncated) || truncated.ValueKind != JsonValueKind.False)
            throw new InvalidOperationException("Query result is truncated or completeness is unknown");
        var rows = root.GetProperty("rows");
        if (rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() != expected.Rows.Length)
            throw new InvalidOperationException("Query row count mismatch");
        for (var r = 0; r < expected.Rows.Length; r++)
        {
            var row = rows[r];
            if (row.ValueKind != JsonValueKind.Object || row.EnumerateObject().Count() != expected.Columns.Length)
                throw new InvalidOperationException($"Query column count mismatch at row {r}");
            for (var c = 0; c < expected.Columns.Length; c++)
            {
                if (!row.TryGetProperty(expected.Columns[c], out var value) || !Equal(value, expected.Rows[r][c]))
                    throw new InvalidOperationException($"Query mismatch at row {r}, column {expected.Columns[c]}");
            }
        }
    }
    private static bool Equal(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind) return false;
        return a.ValueKind switch
        {
            JsonValueKind.Number => a.TryGetDecimal(out var x) && b.TryGetDecimal(out var y) ? x == y : a.GetRawText() == b.GetRawText(),
            JsonValueKind.String => a.GetString() == b.GetString(),
            JsonValueKind.Null or JsonValueKind.True or JsonValueKind.False => true,
            _ => throw new ArgumentException("Expected query cells must be scalar JSON values")
        };
    }
}
