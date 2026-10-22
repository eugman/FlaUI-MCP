using System.Text.Json;
using FlaUI.Mcp;
using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// A paste whose read-back shows it did not apply is a failure, not ok with a warning; and a refusal recorded
/// as coming after input keeps actionDispatched: true even when this call counted no input itself.
/// </summary>
public sealed class PasteAndRefusalStatusTests
{
    [Theory]
    [InlineData("unchanged", false)]
    [InlineData("not-applied", true)]
    [InlineData("mismatch", true)]
    [InlineData("verified", false)]
    [InlineData("not-established", false)]
    public void APasteThatDidNotApplyIsAFailure(string verification, bool failed)
    {
        var reply = PasteTool.PasteReply("Sent Ctrl+V for 3 characters to w1e1.", new(verification, " sentence"));

        Assert.Equal(failed, reply.IsError == true);
        Assert.Equal("completed", reply.Outcome!.Dispatch);
        Assert.Equal(verification, reply.Outcome.Verification);
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
