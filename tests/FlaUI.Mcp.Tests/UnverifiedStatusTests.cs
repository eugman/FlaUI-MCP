using System.Text.Json;
using FlaUI.Mcp;
using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>An unverified result never reads as ok: not in operation status, and not rolled up into a batch.</summary>
public sealed class UnverifiedStatusTests
{
    private static McpToolResult Unverified() =>
        new()
        {
            Content = [new() { Type = "text", Text = "Sent" }],
            Outcome = new ToolOutcome("completed", "not-tracked").WithProof(
                new Proof(Proof.Unavailable, null, "x", "Value", true)
            ),
        };

    private sealed class Answers(McpToolResult result) : ToolBase
    {
        public override string Name => "answers";
        public override string Description => "no desktop";
        public override object InputSchema => new { };

        public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments) => Task.FromResult(result);
    }

    [Fact]
    public async Task OperationStatusReportsUnverified()
    {
        var registry = new ToolRegistry();
        registry.RegisterTool(new Answers(Unverified()));

        var result = await registry.ExecuteToolAsync("answers", null);
        var status = JsonSerializer.SerializeToElement(registry.Operations.Status(result.Outcome!.OperationId));

        Assert.Equal("unverified", result.Outcome.Status);
        Assert.Equal("unverified", status[0].GetProperty("status").GetString());
    }

    private static McpToolResult Text(ToolOutcome? outcome = null) =>
        new() { Content = [new() { Type = "text", Text = "Done" }], Outcome = outcome };

    [Fact]
    public void InputSentWithoutAProofIsUnverified()
    {
        // The input went out, but nothing checked its effect: not-established, so not ok.
        var registryMade = ToolRegistry.WithDispatch(Text(), 1).Outcome!;
        var toolMade = ToolRegistry.WithDispatch(Text(new ToolOutcome("completed", "returned")), 2).Outcome!;

        Assert.Equal("unverified", registryMade.Status);
        Assert.Equal("not-established", registryMade.Verification);
        Assert.Equal("unverified", toolMade.Status);
    }

    [Fact]
    public void ACallThatSentNothingIsOkWithoutAProof()
    {
        // A read, or a mutating tool that found nothing to do: there is no effect to check.
        Assert.Equal("ok", ToolRegistry.WithDispatch(Text(), 0).Outcome!.Status);
        Assert.Equal(
            "ok",
            ToolRegistry.WithDispatch(Text(new ToolOutcome("completed", "returned")), 0).Outcome!.Status
        );
    }

    [Fact]
    public void AProofStillDecidesWhenThereIsOne()
    {
        var verified = Text(
            new ToolOutcome("completed", "returned").WithProof(new Proof(Proof.Verified, "x", "x", "Value", true))
        );

        Assert.Equal("ok", ToolRegistry.WithDispatch(verified, 1).Outcome!.Status);
    }

    [Fact]
    public async Task ABatchWithAnUnverifiedStepIsUnverified()
    {
        var rows = JsonSerializer.Deserialize<JsonElement[]>("""[{"action":"fill","ref":"w1e1","value":"x"}]""")!;
        var step = ToolRegistry.WithDispatch(Unverified(), 1);

        var batch = await BatchTool.ExecuteRows(rows, (_, _) => Task.FromResult(step));

        Assert.NotEqual(true, batch.IsError);
        Assert.Equal("unverified", batch.Outcome!.Status);
        Assert.Equal("unverified", ToolRegistry.WithDispatch(batch, 1).Outcome!.Status);
    }
}
