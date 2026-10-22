using System.Text.Json;
using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>A host's own structured fields sit at the top level of structuredContent, never over the fixed ones.</summary>
public sealed class ToolOutcomeDetailsTests
{
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);

    private static JsonElement Structured(ToolOutcome outcome) =>
        JsonSerializer
            .SerializeToElement(new McpToolResult { Content = [new() { Text = "walked" }], Outcome = outcome })
            .GetProperty("structuredContent");

    [Fact]
    public void DetailsAreWrittenAtTheTopLevelBesideTheFixedFields()
    {
        var outcome = new ToolOutcome("completed", "returned") with
        {
            Status = "ok",
            Details = new Dictionary<string, JsonElement> { ["walk"] = Json(new { steps = 3, last = "Save" }) },
        };

        var structured = Structured(outcome);

        Assert.Equal(3, structured.GetProperty("walk").GetProperty("steps").GetInt32());
        Assert.Equal("Save", structured.GetProperty("walk").GetProperty("last").GetString());
        Assert.Equal("ok", structured.GetProperty("status").GetString());
        Assert.Equal("walked", structured.GetProperty("message").GetString());
        Assert.False(structured.TryGetProperty("details", out _));
        Assert.False(structured.TryGetProperty("Details", out _));
    }

    [Fact]
    public void NoDetailsWritesNothingExtra()
    {
        var structured = Structured(new ToolOutcome("completed", "returned"));

        string[] fixedFields = ["dispatch", "provider", "pendingPatternId", "modalTitle", "operationId", "message"];
        Assert.All(structured.EnumerateObject(), p => Assert.Contains(p.Name, fixedFields));
    }

    [Theory]
    [InlineData("status")]
    [InlineData("dispatch")]
    [InlineData("message")]
    [InlineData("proof")]
    [InlineData("steps")]
    [InlineData("failure")]
    [InlineData("operationId")]
    public void ADetailCannotTakeAFixedFieldsName(string key)
    {
        var error = Assert.Throws<ArgumentException>(() =>
            new ToolOutcome("completed", "returned") with
            {
                Details = new Dictionary<string, JsonElement> { [key] = Json("forged") },
            }
        );

        Assert.Contains(key, error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Walk")]
    [InlineData("walk steps")]
    [InlineData("walk-steps")]
    [InlineData("$walk")]
    public void ADetailKeyMustBeACamelCaseName(string key)
    {
        Assert.Throws<ArgumentException>(() =>
            new ToolOutcome("completed", "returned") with
            {
                Details = new Dictionary<string, JsonElement> { [key] = Json(1) },
            }
        );
    }

    [Fact]
    public void ChangingTheCallersDictionaryAfterwardsChangesNothing()
    {
        var source = new Dictionary<string, JsonElement> { ["walk"] = Json(1) };
        var outcome = new ToolOutcome("completed", "returned") with { Details = source };

        source["status"] = Json("forged");

        var structured = Structured(outcome with { Status = "failed" });
        Assert.Equal("failed", structured.GetProperty("status").GetString());
        Assert.False(outcome.Details!.ContainsKey("status"));
    }

    [Fact]
    public void DetailsSurviveTheMessageCopy()
    {
        var outcome = new ToolOutcome("completed", "returned") with
        {
            Details = new Dictionary<string, JsonElement> { ["walk"] = Json(2) },
        };

        Assert.Equal(
            2,
            Structured(outcome.WithProof(new Proof(Proof.Verified, "x", "x", "Value", true)))
                .GetProperty("walk")
                .GetInt32()
        );
    }
}
