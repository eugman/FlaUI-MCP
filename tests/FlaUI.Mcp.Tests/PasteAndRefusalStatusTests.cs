using System.Text.Json;
using FlaUI.Mcp;
using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// A paste reply maps an explicit failure verdict to an error; a refusal recorded
/// as coming after input keeps actionDispatched: true even when this call counted no input itself.
/// </summary>
public sealed class PasteAndRefusalStatusTests
{
    [Theory]
    [InlineData("unchanged", false, "unverified")]
    [InlineData("mismatch", true, "failed")]
    [InlineData("verified", false, "ok")]
    [InlineData("not-established", false, "unverified")]
    [InlineData("unavailable", false, "unverified")]
    public void PasteReplyMapsVerificationToErrorAndStatus(string verification, bool failed, string status)
    {
        var reply = PasteTool.PasteReply("Sent Ctrl+V for 3 characters to w1e1.", new(verification, " sentence"));

        Assert.Equal(failed, reply.IsError == true);
        Assert.Equal("completed", reply.Outcome!.Dispatch);
        Assert.Equal(verification, reply.Outcome.Verification);
        // A value that did not change proves nothing about where Ctrl+V went: unverified, never ok.
        Assert.Equal(verification, reply.Outcome.Proof!.Verdict);
        Assert.Equal(status, Proof.StatusFor(reply.Outcome.Proof.Verdict));
    }

    [Fact]
    public async Task ARecordedActionDispatchedIsNotOverwrittenByThisCallsCount()
    {
        var registry = new ToolRegistry();
        registry.RegisterTool(new RefusesAfterEarlierInput());

        var result = await registry.ExecuteToolAsync("refuses", null);

        Assert.Equal(0, result.Outcome!.InputsSent);
        Assert.True(result.Outcome.Failure!.ActionDispatched);
    }

    private sealed class RefusesAfterEarlierInput : ToolBase
    {
        public override string Name => "refuses";
        public override string Description => "no desktop";
        public override object InputSchema => new { };

        public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
        {
            // A refusal raised after a Select that another layer counted (as the click fallback records).
            _ = InputRefusedException.Record(
                new InputRefusal("point-not-on-target", "covered") { ActionDispatched = true }
            );
            return Task.FromResult(ErrorResult("covered"));
        }
    }
}
