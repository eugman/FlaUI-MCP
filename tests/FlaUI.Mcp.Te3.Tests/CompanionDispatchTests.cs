using System.Text.Json;
using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>A companion failure says whether any input went out before it, so the caller knows what to inspect.</summary>
public sealed class CompanionDispatchTests
{
    private static JsonElement Reply(FlaUI.Mcp.McpToolResult result) =>
        JsonDocument.Parse(result.Content[0].Text!).RootElement;

    [Fact]
    public void AFailureAfterInputSaysInputWasSent()
    {
        var reply = Reply(Te3Companion.Failure("navigate", "search", "Search text 'Date' was not found", 3));

        Assert.True(reply.GetProperty("actionDispatched").GetBoolean());
        Assert.Equal(3, reply.GetProperty("inputsSent").GetInt32());
    }

    [Fact]
    public void AFailureBeforeAnyInputSaysNoneWasSent()
    {
        var reply = Reply(Te3Companion.Failure("attach", "validate request", "Tabular Editor 3 is not running.", 0));

        Assert.False(reply.GetProperty("actionDispatched").GetBoolean());
        Assert.Equal(0, reply.GetProperty("inputsSent").GetInt32());
    }

    [Fact]
    public async Task ARefusedRequestReportsNoInputSent()
    {
        var reply = Reply(
            await new Te3Companion(null!, Te3Companion.Navigate).ExecuteAsync(
                JsonSerializer.SerializeToElement(new { })
            )
        );

        Assert.Equal("validate", reply.GetProperty("phase").GetString());
        Assert.False(reply.GetProperty("actionDispatched").GetBoolean());
        Assert.Equal(0, reply.GetProperty("inputsSent").GetInt32());
    }
}
