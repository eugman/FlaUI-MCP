using System.Drawing;
using System.Text.Json;
using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// A capture that parked the pointer first is a read that succeeded; the park is its own detail. A park that left
/// the pointer over the window makes the capture unverified (a hover state may show), never failed.
/// </summary>
public sealed class ParkOutcomeTests
{
    private static readonly Rectangle Window = new(0, 0, 100, 100);

    private static McpToolResult Captured() => new() { Content = [new() { Type = "image", Data = "AA==" }] };

    [Fact]
    public void AVerifiedParkIsTheCapturesProof()
    {
        var result = ScreenshotChecks.WithPark(Captured(), ScreenshotTool.ParkProof(Window, new Point(200, 50)));

        var outcome = ToolRegistry.WithDispatch(result, 1).Outcome!;
        Assert.Equal("ok", outcome.Status);
        Assert.Equal(Proof.Verified, outcome.Proof?.Verdict);
        Assert.Equal(Proof.Verified, outcome.Details!["park"].GetProperty("verdict").GetString());
    }

    [Fact]
    public void AParkThatLandedOnTheWindowLeavesTheCaptureUnverifiedNotFailed()
    {
        var result = ScreenshotChecks.WithPark(Captured(), ScreenshotTool.ParkProof(Window, new Point(50, 50)));

        var outcome = ToolRegistry.WithDispatch(result, 1).Outcome!;
        Assert.NotEqual(true, result.IsError);
        Assert.Equal("unverified", outcome.Status);
        Assert.Null(outcome.Proof);
        Assert.Equal(Proof.Mismatch, outcome.Details!["park"].GetProperty("verdict").GetString());
        Assert.Contains(
            result.Content,
            c => c.Text?.Contains("still over the window", StringComparison.Ordinal) == true
        );
    }

    [Fact]
    public void ARefusedParkLeavesAnOkCaptureWithTheParkAsADetail()
    {
        var result = ScreenshotChecks.WithPark(Captured(), ScreenshotTool.ParkProof(Window, null));

        var outcome = ToolRegistry.WithDispatch(result, 0).Outcome!;
        Assert.Equal("ok", outcome.Status);
        Assert.Equal(Proof.Unavailable, outcome.Details!["park"].GetProperty("verdict").GetString());
    }

    [Fact]
    public async Task AParkMismatchIsNotReportedAsAFailureAfterInput()
    {
        var registry = new ToolRegistry();
        var failed = new List<string>();
        registry.FailedAfterInput = (tool, _) => failed.Add(tool);
        registry.RegisterTool(
            new Parks(
                "parks",
                () =>
                {
                    MutationGuard.RecordDispatch();
                    return ScreenshotChecks.WithPark(Captured(), ScreenshotTool.ParkProof(Window, new Point(50, 50)));
                }
            )
        );

        var result = await registry.ExecuteToolAsync("parks", null);

        Assert.Equal("unverified", result.Outcome!.Status);
        Assert.Empty(failed);
    }

    private sealed class Parks(string name, Func<McpToolResult> run) : ToolBase
    {
        public override string Name => name;
        public override string Description => "no desktop";
        public override object InputSchema => new { };

        public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments) => Task.FromResult(run());
    }
}
