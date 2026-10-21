using System.Text.Json;
using Xunit;

/// <summary>What a failed apply-preset reports: each step's measured result and the layout read back after it.</summary>
public sealed class PresetReportTests
{
    private static readonly object Missed = new
    {
        title = "TOM Explorer",
        edge = "left",
        requested = 480,
        reached = 760,
        attempts = 1,
    };

    private static Func<Task<FlaUI.Mcp.McpToolResult>> Drag(int inputsSent) =>
        () =>
            Task.FromResult(
                new FlaUI.Mcp.McpToolResult
                {
                    Content = [new() { Text = "Dragged" }],
                    Outcome = new FlaUI.Mcp.Core.ToolOutcome(
                        FlaUI.Mcp.Core.ToolOutcome.DispatchFor(false, inputsSent),
                        "not-tracked"
                    )
                    {
                        Status = "ok",
                        InputsSent = inputsSent,
                    },
                }
            );

    [Fact]
    public async Task AStepThatMeasuredItsMissReportsTheMeasurementNotJustItsName()
    {
        var inputs = new InputTally();
        var sent = new List<object>();

        await Assert.ThrowsAsync<LayoutSteps.StepFailedException>(() =>
            LayoutSteps.Sending(
                inputs,
                sent.Add,
                "TOM Explorer",
                async () =>
                {
                    await inputs.Invoke(Drag(15), tool: "windows_mouse");
                    throw new LayoutSteps.StepFailedException("missed", Missed);
                }
            )
        );

        Assert.Equal(15, inputs.Sent);
        Assert.Same(Missed, Assert.Single(sent));
    }

    private static async Task<LayoutSteps.ProgressException> FailedPreset()
    {
        var steps = new Queue<string>(["AI Assistant", "TOM Explorer", "Properties"]);
        return await Assert.ThrowsAsync<LayoutSteps.ProgressException>(() =>
            LayoutSteps.Progress(
                "changes",
                sent =>
                {
                    var pane = steps.Dequeue();
                    if (pane == "AI Assistant")
                    {
                        return Task.FromResult<object?>(new { title = pane, reached = 680 });
                    }

                    sent(Missed);
                    throw new LayoutSteps.StepFailedException("Resize of 'TOM Explorer' did not reach", Missed);
                }
            )
        );
    }

    [Fact]
    public async Task AFailedPresetCarriesEachStepAndTheLayoutReadBackAfterTheFailure()
    {
        var failure = LayoutSteps.ReadBack(await FailedPreset(), () => new[] { new { title = "TOM Explorer" } });

        var reply = JsonDocument.Parse(Te3Layout.Failure(failure, 30).Content[0].Text!).RootElement;

        var changes = reply.GetProperty("changes");
        Assert.Equal(680, changes[0].GetProperty("reached").GetInt32());
        var attempted = changes[1].GetProperty("attempted");
        Assert.Equal(480, attempted.GetProperty("requested").GetInt32());
        Assert.Equal(760, attempted.GetProperty("reached").GetInt32());
        Assert.Equal(1, attempted.GetProperty("attempts").GetInt32());
        Assert.Equal(2, changes.GetArrayLength());
        var readBack = reply.GetProperty("layoutAfterFailure");
        Assert.True(readBack.GetProperty("readBack").GetBoolean());
        Assert.Equal("TOM Explorer", readBack.GetProperty("panes")[0].GetProperty("title").GetString());
        Assert.Contains("read back", reply.GetProperty("error").GetString());
    }

    [Fact]
    public async Task AReadBackThatFailsSaysSoInsteadOfHidingTheStepFailure()
    {
        var failure = LayoutSteps.ReadBack(
            await FailedPreset(),
            () => throw new System.Runtime.InteropServices.COMException("element gone")
        );

        var reply = JsonDocument.Parse(Te3Layout.Failure(failure, 30).Content[0].Text!).RootElement;

        var readBack = reply.GetProperty("layoutAfterFailure");
        Assert.False(readBack.GetProperty("readBack").GetBoolean());
        Assert.Contains("element gone", readBack.GetProperty("error").GetString());
        Assert.Contains("did not reach", reply.GetProperty("error").GetString());
        Assert.Equal("partial", Te3Layout.Failure(failure, 30).Outcome?.Dispatch);
    }
}
