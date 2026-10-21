using System.Text.Json;
using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>
/// A companion failure says, in Core's structured outcome, whether any input went out before it, so the caller knows
/// what to inspect. The text keeps the TE3 guidance and no longer repeats the counts.
/// </summary>
public sealed class CompanionDispatchTests
{
    private static JsonElement Reply(FlaUI.Mcp.McpToolResult result) =>
        JsonDocument.Parse(result.Content[0].Text!).RootElement;

    private static void AssertNoTextCounts(FlaUI.Mcp.McpToolResult result)
    {
        Assert.False(Reply(result).TryGetProperty("actionDispatched", out _));
        Assert.False(Reply(result).TryGetProperty("inputsSent", out _));
    }

    [Fact]
    public void AFailureAfterInputIsPartial()
    {
        var result = Te3Companion.Failure(
            "navigate",
            "search",
            new InvalidOperationException("Search text 'Date' was not found"),
            3
        );

        Assert.Equal("partial", result.Outcome?.Dispatch);
        Assert.Equal(3, result.Outcome?.InputsSent);
        Assert.Equal("failed", result.Outcome?.Status);
        AssertNoTextCounts(result);
    }

    [Fact]
    public void AFailureBeforeAnyInputIsNotDispatched()
    {
        var result = Te3Companion.Failure(
            "attach",
            "validate request",
            new InvalidOperationException("Tabular Editor 3 is not running."),
            0
        );

        Assert.Equal("not-dispatched", result.Outcome?.Dispatch);
        Assert.Equal(0, result.Outcome?.InputsSent);
        AssertNoTextCounts(result);
    }

    [Fact]
    public void AFailureAtAPendingCallIsNeverSafeToRetry()
    {
        var pending = new CoreCallException(
            "windows_click",
            new FlaUI.Mcp.McpToolResult
            {
                IsError = true,
                Content = [new() { Text = "Not sent yet: the request is still waiting on the app." }],
                Outcome = new ToolOutcome("timed_out_pending", "pending") { Status = "failed", InputsSent = 0 },
            }
        );

        var result = Te3Companion.Failure("navigate", "click", pending, 0);

        Assert.Equal("timed_out_pending", result.Outcome?.Dispatch);
        Assert.Equal("pending", result.Outcome?.Provider);
        Assert.True(result.Outcome?.IsPending);
    }

    [Fact]
    public void AFailureCarriesTheRefusalAsFields()
    {
        var refused = new InputRefusedException(new InputRefusal("activation-denied", "Windows kept another window."));

        var result = Te3Companion.Failure("navigate", "Open Model menu", refused, 2);

        Assert.Equal("activation-denied", result.Outcome?.Failure?.Code);
        Assert.True(result.Outcome?.Failure?.ActionDispatched);
    }

    [Fact]
    public async Task ARefusedRequestReportsNoInputSent()
    {
        var result = await new Te3Companion(null!, Te3Companion.Navigate).ExecuteAsync(
            JsonSerializer.SerializeToElement(new { })
        );

        Assert.Equal("validate", Reply(result).GetProperty("phase").GetString());
        Assert.Equal("not-dispatched", result.Outcome?.Dispatch);
        Assert.Equal(0, result.Outcome?.InputsSent);
    }
}
