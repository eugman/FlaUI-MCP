using System.Text.Json;
using FlaUI.Mcp.Core;
using Xunit;
using static CoreResults;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>
/// Tests written to kill mutants that survived in the Core-outcome adaptation (CoreCallException, InputTally,
/// NavigationRetry, the grant and layout tools); see docs/mutation-te3.md. Scripted results; nothing is sent.
/// </summary>
public sealed class AdaptationMutationTests
{
    [Fact]
    public void ACoreCallFailureKeepsEveryLineOfTheToolsText() =>
        Assert.Equal(
            "first; second",
            new CoreCallException("windows_click", Result(null, true, "first", "second")).Message
        );

    [Fact]
    public void APlainFailureIsNotPendingAndEstablishesNothing()
    {
        var outcome = CoreFailure.Reply(new InvalidOperationException("Pane is gone"), 0);

        Assert.Equal("not-dispatched", outcome.Dispatch);
        Assert.Equal("not-tracked", outcome.Provider);
        Assert.Equal("not-established", outcome.Verification);
        // Typed for the broker: a precondition failed before any input, so nothing was dispatched.
        Assert.Equal(CoreCallException.PreconditionFailed, outcome.Failure?.Code);
        Assert.False(outcome.Failure?.ActionDispatched);
    }

    [Theory]
    [InlineData("completed", "pending", "partial")]
    [InlineData("timed_out_pending", "unknown", "timed_out_pending")]
    public void AFailingCallPreservesKnownDispatchOrPendingState(string dispatch, string provider, string expected) =>
        Assert.Equal(expected, CoreFailure.Reply(Failed(dispatch, "Failed.", provider: provider), 0).Dispatch);

    [Fact]
    public void ARefusalBeforeAnyInputSaysNoActionWasDispatched()
    {
        var refused = new InputRefusedException(new InputRefusal("point-not-on-target", "Covered."));

        var outcome = CoreFailure.Reply(refused, 0);

        Assert.False(outcome.Failure?.ActionDispatched);
        Assert.Equal("point-not-on-target", outcome.Failure?.Code);
    }

    [Fact]
    public async Task AStoppedOperationRunsNoFurtherCoreCall()
    {
        var operation = new OperationContext();
        operation.Stop.Cancel();
        OperationContext.Current.Value = operation;
        var called = false;
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                new InputTally().Invoke(() =>
                {
                    called = true;
                    return Task.FromResult(Result(null, error: false));
                })
            );
        }
        finally
        {
            OperationContext.Current.Value = null;
        }

        Assert.False(called);
    }

    [Fact]
    public async Task ANestedCallCountsWhatTheRunningOperationRecordedForIt()
    {
        // Inside a te3_* tool call Core counts the step against the running operation, which had input before.
        var operation = new OperationContext();
        for (var i = 0; i < 3; i++)
        {
            operation.RecordInput();
        }

        OperationContext.Current.Value = operation;
        var inputs = new InputTally();
        try
        {
            await inputs.Invoke(() =>
            {
                operation.RecordInput();
                operation.RecordInput();
                return Task.FromResult(Result(new ToolOutcome("completed", "not-tracked") { Status = "ok" }, false));
            });
            inputs.Dispatch(operation.RecordInput);
        }
        finally
        {
            OperationContext.Current.Value = null;
        }

        Assert.Equal(3, inputs.Sent);
        Assert.Equal(6, operation.InputsSent);
    }

    [Fact]
    public async Task AnErrorWithoutAFailedStatusStillThrows() =>
        await Assert.ThrowsAsync<CoreCallException>(() =>
            new InputTally().Invoke(() => Task.FromResult(Result(new ToolOutcome("not-dispatched", "not-tracked"))))
        );

    [Fact]
    public async Task AFailedStatusWithoutTheErrorFlagStillThrows() =>
        await Assert.ThrowsAsync<CoreCallException>(() =>
            new InputTally().Invoke(() =>
                Task.FromResult(Result(new ToolOutcome("completed", "not-tracked") { Status = "failed" }, false))
            )
        );

    [Fact]
    public async Task AnAttemptIsMeasuredFromWhereTheTallyStood()
    {
        // Earlier steps sent five inputs; this attempt sent none, so its not-dispatched failure is retried.
        var attempts = 0;

        await NavigationRetry.Once(
            () =>
                attempts++ == 0
                    ? throw Failed("not-dispatched", "Failed.", provider: "not-tracked")
                    : Task.CompletedTask,
            () => 5,
            null
        );

        Assert.Equal(2, attempts);
    }

    private static JsonElement Schema(ToolBase tool) => JsonSerializer.SerializeToElement(tool.InputSchema);

    [Fact]
    public void BeginAndEndDescribeThemselves()
    {
        using var interaction = new Te3Interaction(() => 0);
        var begin = new Te3InteractionTool(interaction, Te3InteractionTool.Begin);
        var end = new Te3InteractionTool(interaction, Te3InteractionTool.End);

        Assert.False(begin.Annotations.IdempotentHint);
        Assert.True(end.Annotations.IdempotentHint);
        Assert.Contains("Begin", begin.Description);
        Assert.StartsWith("End", end.Description);
        Assert.Equal(
            ["batch", "userApproved"],
            Schema(begin).GetProperty("required").EnumerateArray().Select(r => r.GetString())
        );
        Assert.False(Schema(end).TryGetProperty("required", out _));
        Assert.False(Schema(begin).GetProperty("additionalProperties").GetBoolean());
        Assert.False(Schema(end).GetProperty("additionalProperties").GetBoolean());
    }

    private static async Task<string> LayoutRefusal(bool readOnly, string action)
    {
        var result = await new Te3Layout(null!, readOnly: readOnly).ExecuteAsync(
            JsonSerializer.SerializeToElement(new { action })
        );
        Assert.True(result.IsError);
        Assert.Equal("text", result.Content[0].Type);
        return result.Content[0].Text!;
    }

    [Fact]
    public async Task EachLayoutToolNamesTheOtherForItsActions()
    {
        Assert.Contains("only reads; resize is a te3_layout action", await LayoutRefusal(true, "resize"));
        Assert.Contains("list sends no input; call te3_layout_read", await LayoutRefusal(false, "list"));
        Assert.Contains("Unsupported TE3 layout action", await LayoutRefusal(true, "dance"));
        Assert.Contains("Unsupported TE3 layout action", await LayoutRefusal(false, "dance"));
    }

    [Fact]
    public void TheLayoutToolsDescribeWhatTheyDo()
    {
        Assert.Contains("sends no input and needs no grant", new Te3Layout(null!, readOnly: true).Description);
        Assert.Contains("needs an active te3_interaction_begin grant", new Te3Layout(null!).Description);
        Assert.False(Schema(new Te3Layout(null!)).GetProperty("additionalProperties").GetBoolean());
    }

    [Fact]
    public void TheCompanionSchemasRefuseUnknownArguments()
    {
        foreach (var name in Te3Companion.Names)
        {
            Assert.False(Schema(new Te3Companion(null!, name)).GetProperty("additionalProperties").GetBoolean());
        }

        Assert.False(
            Schema(FlaUI.Mcp.Te3.Te3NativePreflightTool.Create()).GetProperty("additionalProperties").GetBoolean()
        );
    }

    [Fact]
    public void TheCancelSequenceTriesCancelThenTheTitleBarClose()
    {
        Assert.Equal("Cancel", DialogCancel.Buttons[0].Name);
        Assert.Equal("Button", DialogCancel.Buttons[0].ControlType);
        Assert.Equal(
            ("Close", "Close", "Button"),
            (DialogCancel.Buttons[1].Name, DialogCancel.Buttons[1].AutomationId, DialogCancel.Buttons[1].ControlType)
        );
    }

    [Fact]
    public async Task AJournaledDirectSendRecordsThatItWasSent()
    {
        var journal = Path.Combine(Path.GetTempPath(), "fla_direct_" + Guid.NewGuid().ToString("N") + ".jsonl");
        try
        {
            await RunJournal.Input(journal, "te3_click", new { }, () => { });

            var entry = JsonDocument.Parse(File.ReadLines(journal).Last()).RootElement;
            Assert.Equal("te3_click sent.", entry.GetProperty("content")[0].GetProperty("text").GetString());
        }
        finally
        {
            File.Delete(journal);
        }
    }

    [Fact]
    public async Task AValidationFailureNamesItsStep()
    {
        var result = await new Te3Companion(null!, Te3Companion.Navigate).ExecuteAsync(
            JsonSerializer.SerializeToElement(new { })
        );

        var reply = JsonDocument.Parse(result.Content[0].Text!).RootElement;
        Assert.Equal("validate request", reply.GetProperty("lastStep").GetString());
    }
}
