using System.Drawing;
using System.Text.Json;
using FlaUI.Mcp;
using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests.Contract;

/// <summary>
/// A tool that checks its effect says how in a structured proof (verdict, observed, expected, source, independent),
/// and status follows the verdict: ok only when verified or a no-op, failed on a mismatch, otherwise unverified.
/// Clients and brokers read the verdict instead of the text.
/// </summary>
public sealed class ProofContractTests
{
    [Theory]
    [InlineData("a", "a", "verified")]
    [InlineData("a", "b", "mismatch")]
    [InlineData("a", null, "unavailable")]
    public void CompareJudgesObservedAgainstExpected(string expected, string? observed, string verdict)
    {
        var proof = Proof.Compare(expected, observed, "Value", independent: true);

        Assert.Equal(verdict, proof.Verdict);
        Assert.Equal(expected, proof.Expected);
        Assert.Equal(observed, proof.Observed);
        Assert.Equal("Value", proof.Source);
        Assert.True(proof.Independent);
    }

    [Theory]
    [InlineData("verified", false, "ok")]
    [InlineData("no-op", false, "ok")]
    [InlineData("unavailable", false, "unverified")]
    [InlineData("not-established", false, "unverified")]
    [InlineData("mismatch", true, "failed")]
    public void StatusFollowsTheVerdict(string verdict, bool isError, string status)
    {
        var result = new McpToolResult
        {
            Content = [new() { Type = "text", Text = "done" }],
            IsError = isError ? true : null,
            Outcome = new ToolOutcome("completed", "not-tracked").WithProof(new Proof(verdict, "x", "y", "test", true)),
        };

        var outcome = ToolRegistry.WithDispatch(result, 1).Outcome!;

        Assert.Equal(status, outcome.Status);
        Assert.Equal(verdict, outcome.Verification);
        Assert.Equal(
            verdict,
            JsonSerializer.SerializeToElement(outcome).GetProperty("proof").GetProperty("verdict").GetString()
        );
    }

    [Fact]
    public void APlacementProvesWhereTheWindowLanded()
    {
        var landed = WindowPlacementTool.PlacementResult(
            new(10, 10, 400, 300),
            new(0, 0, 100, 100),
            new(10, 10, 400, 300)
        );
        var clamped = WindowPlacementTool.PlacementResult(
            new(10, 10, 400, 300),
            new(0, 0, 100, 100),
            new(10, 10, 440, 300)
        );

        Assert.Equal("verified", landed.Outcome!.Proof!.Verdict);
        Assert.Equal("10,10 400x300", landed.Outcome.Proof.Observed);
        Assert.Equal("mismatch", clamped.Outcome!.Proof!.Verdict);
        Assert.Equal("10,10 440x300", clamped.Outcome.Proof.Observed);
        Assert.Equal("placement-mismatch", clamped.Outcome.Failure!.Code);
    }

    [Fact]
    public void AFillProvesTheValueItReadBack()
    {
        var reply = FillTool.FillReply(
            "field",
            "new",
            new TextObservation("old", "Text"),
            new("completed", "returned")
        );

        Assert.Equal(new Proof("mismatch", "old", "new", "Text", true), reply.Outcome!.Proof);
    }

    [Fact]
    public void AnExpandProvesTheStateItReadBack()
    {
        var reply = ClickTool.CheckedPatternResult(
            new PatternCallResult(PatternCallOutcome.Completed),
            ClickTool.ExpandVerdict("Tables", "expand", FlaUI.Core.Definitions.ExpandCollapseState.Expanded),
            "ExpandCollapse",
            "Expanded"
        );

        Assert.Equal(new Proof("self-reported", "Expanded", "Expanded", "ExpandCollapse", false), reply.Outcome!.Proof);
        Assert.Equal("unverified", ToolRegistry.WithDispatch(reply, 1).Outcome!.Status);
    }

    [Fact]
    public void APasteProvesWhatTheControlHoldsNow()
    {
        var reply = PasteTool.PasteReply("Sent", PasteTool.Judge("", "pasted", "pasted"), "pasted", "pasted");

        Assert.Equal(new Proof("verified", "pasted", "pasted", "Value", true), reply.Outcome!.Proof);
        Assert.Equal("ok", ToolRegistry.WithDispatch(reply, 2).Outcome!.Status);
    }

    [Theory]
    [InlineData(900, 500, "verified")]
    [InlineData(150, 150, "mismatch")]
    public void AParkProvesThePointerLeftTheWindow(int x, int y, string verdict) =>
        Assert.Equal(verdict, ScreenshotTool.ParkProof(new Rectangle(100, 100, 600, 400), new Point(x, y)).Verdict);

    [Fact]
    public void ARefusedParkIsUnverified() =>
        Assert.Equal("unavailable", ScreenshotTool.ParkProof(new Rectangle(100, 100, 600, 400), null).Verdict);
}
