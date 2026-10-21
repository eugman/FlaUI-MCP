using Xunit;

/// <summary>Pane title matching for te3_layout; no desktop access.</summary>
public sealed class Te3LayoutTests
{
    [Fact]
    public void LayoutCloseNeverTargetsDocumentTabs()
    {
        Assert.True(Te3Layout.CanCloseKind("pane"));
        Assert.False(Te3Layout.CanCloseKind("document"));
    }

    [Fact]
    public void AssistantReviewPresetRequiresPresentationSize()
    {
        var sizes = FlaUI.Mcp.Te3.Te3PanePlan.Resolve(FlaUI.Mcp.Te3.Te3PanePlan.AssistantModelReview, 1920, 1080);
        Assert.Equal(680, sizes.Single(s => s.Pane == "AI Assistant").Width);
        Assert.Equal(480, sizes.Single(s => s.Pane == "TOM Explorer").Width);
        Assert.Equal(320, sizes.Single(s => s.Pane == "Properties").Height);
        Assert.Throws<ArgumentException>(() =>
            FlaUI.Mcp.Te3.Te3PanePlan.Resolve(FlaUI.Mcp.Te3.Te3PanePlan.AssistantModelReview, 1600, 900)
        );
    }

    [Fact]
    public void PresetGuardAlwaysLeavesTheMinimumEditorWidth()
    {
        // Calls the real guard: the narrowest accepted window, minus every side pane and the chrome reserve,
        // is exactly the minimum editor width, and one pixel less is refused.
        var preset = FlaUI.Mcp.Te3.Te3PanePlan.AssistantModelReview;
        var probe = FlaUI.Mcp.Te3.Te3PanePlan.Resolve(preset, 1920, 1080);
        var narrowest = FlaUI.Mcp.Te3.Te3PanePlan.MinimumWindowWidth(probe);
        var sizes = FlaUI.Mcp.Te3.Te3PanePlan.Resolve(preset, narrowest, 900);
        var editor = narrowest - sizes.Sum(s => s.Width ?? 0) - FlaUI.Mcp.Te3.Te3PanePlan.ChromeReserve;
        Assert.Equal(FlaUI.Mcp.Te3.Te3PanePlan.MinimumEditorWidth, editor);
        Assert.Throws<ArgumentException>(() => FlaUI.Mcp.Te3.Te3PanePlan.Resolve(preset, narrowest - 1, 900));
        Assert.Throws<ArgumentException>(() => FlaUI.Mcp.Te3.Te3PanePlan.Resolve(preset, narrowest, 899));
    }

    [Theory]
    [InlineData("TOM Explorer", "TOM Explorer")]
    [InlineData("TOM Explorer (Changed)", "TOM Explorer")]
    [InlineData("TOM Explorer (Changed)", "TOM Explorer (Changed)")]
    [InlineData("Properties", "Properties (Changed)")]
    public void ShowChangesSuffixIsIgnored(string pane, string requested) =>
        Assert.True(Te3Layout.TitleMatches(pane, requested));

    [Theory]
    [InlineData("TOM Explorer", "Properties")]
    [InlineData("TOM Explorer", "tom explorer")]
    [InlineData("Best Practice Analyzer", "Best Practice")]
    public void OtherTitlesDoNotMatch(string pane, string requested) =>
        Assert.False(Te3Layout.TitleMatches(pane, requested));

    [Fact]
    public async Task PlanPresetDoesNotRequireTe3Process()
    {
        var tool = new Te3Layout(null!, readOnly: true);
        var args = System.Text.Json.JsonSerializer.SerializeToElement(
            new
            {
                action = "plan-preset",
                preset = FlaUI.Mcp.Te3.Te3PanePlan.AssistantModelReview,
                windowWidth = 1800,
                windowHeight = 900,
            }
        );
        var result = await tool.ExecuteAsync(args);
        Assert.NotEqual(true, result.IsError);
    }

    [Fact]
    public void LayoutRefsCarryTheWindowIdentity()
    {
        // Parts are found with FindAllDescendants, not ElementQuery, so nothing else records the
        // owner. Without it pattern actions skip PID/start-time checks and the pending-provider and
        // modal guards see PID 0. The form is created but never shown.
        using var host = new FlaUI.Mcp.Core.AutomationHost(FlaUI.Mcp.Core.ProcessPolicy.AllowAll);
        using var form = new System.Windows.Forms.Form();
        var handle = Te3Layout.Bind(host, form.Handle, Environment.ProcessId);
        var reference = host.Elements.Register(handle, null!);
        Assert.Equal(Environment.ProcessId, host.Elements.GetProcessIdForRef(reference));
    }

    [Fact]
    public void CoreRefusesALayoutRefWithoutTheWindowIdentity()
    {
        // Why Bind still records the identity: Core refuses the click rather than recording it itself.
        using var host = new FlaUI.Mcp.Core.AutomationHost(FlaUI.Mcp.Core.ProcessPolicy.AllowAll);
        using var form = new System.Windows.Forms.Form();
        var handle = host.Sessions.RegisterNativeWindow(form.Handle, Environment.ProcessId);
        var reference = host.Elements.Register(handle, host.Sessions.GetWindow(handle)!);

        var error = Assert.Throws<InvalidOperationException>(() => host.Elements.ValidateReference(reference));

        Assert.Contains("no process identity", error.Message);
    }

    [Fact]
    public async Task AFailedStepReportsTheStepsAlreadyDone()
    {
        // keep/close and apply-preset loop over panes; a dialog on the second close used to drop the
        // first from the reply and claim nothing else was closed.
        var panes = new Queue<string>(["Properties", "TOM Explorer", "Messages"]);
        var error = await Record.ExceptionAsync(() =>
            LayoutSteps.Progress(
                "closed",
                () =>
                {
                    var pane = panes.Dequeue();
                    return pane == "TOM Explorer"
                        ? throw new InvalidOperationException(Te3Layout.DialogOpened(pane, "Save changes?"))
                        : Task.FromResult<object?>(pane);
                }
            )
        );
        var result = Te3Layout.Failure(Assert.IsAssignableFrom<InvalidOperationException>(error));
        Assert.True(result.IsError);
        var payload = System.Text.Json.JsonDocument.Parse(result.Content[0].Text!).RootElement;
        Assert.Equal("Properties", Assert.Single(payload.GetProperty("closed").EnumerateArray()).GetString());
        Assert.Contains("Save changes?", payload.GetProperty("error").GetString());
        Assert.DoesNotContain("nothing else", payload.GetProperty("error").GetString());
        Assert.Equal("Messages", Assert.Single(panes));
    }

    private static async Task<System.Text.Json.JsonElement> FailedProgress(Exception failure)
    {
        var steps = new Queue<string>(["Properties", "TOM Explorer"]);
        var error = await Assert.ThrowsAsync<LayoutSteps.ProgressException>(() =>
            LayoutSteps.Progress(
                "closed",
                sent =>
                {
                    var pane = steps.Dequeue();
                    sent(pane);
                    return pane == "TOM Explorer" ? throw failure : Task.FromResult<object?>(pane);
                }
            )
        );
        var result = Te3Layout.Failure(error);
        return System.Text.Json.JsonDocument.Parse(result.Content[0].Text!).RootElement.GetProperty("closed");
    }

    [Fact]
    public async Task AStepWhoseInputWentOutBeforeItFailedIsRecordedAsAttempted()
    {
        var closed = await FailedProgress(
            new InvalidOperationException(Te3Layout.DialogOpened("TOM Explorer", "Save?"))
        );

        Assert.Equal("Properties", closed[0].GetString());
        Assert.Equal("TOM Explorer", closed[1].GetProperty("attempted").GetString());
        Assert.Contains("Save?", closed[1].GetProperty("error").GetString());
    }

    [Fact]
    public async Task AComFailureAfterAStepWasSentKeepsTheProgressRecord()
    {
        var closed = await FailedProgress(new System.Runtime.InteropServices.COMException("element gone"));

        Assert.Equal("Properties", closed[0].GetString());
        Assert.Equal("TOM Explorer", closed[1].GetProperty("attempted").GetString());
    }

    [Fact]
    public async Task CancellationIsNotTurnedIntoALayoutFailure()
    {
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            LayoutSteps.Progress("closed", () => throw new OperationCanceledException("stopped"))
        );
    }

    // A scripted Core result. inputsSent is Core's count: by default one for a success and none for a refusal.
    private static Func<Task<FlaUI.Mcp.McpToolResult>> Click(string text, bool error = false, int? inputsSent = null) =>
        () =>
            Task.FromResult(
                new FlaUI.Mcp.McpToolResult
                {
                    IsError = error,
                    Content = [new() { Text = text }],
                    Outcome = new FlaUI.Mcp.Core.ToolOutcome(
                        FlaUI.Mcp.Core.ToolOutcome.DispatchFor(error, inputsSent ?? (error ? 0 : 1)),
                        "not-tracked"
                    )
                    {
                        Status = error ? "failed" : "ok",
                        InputsSent = inputsSent ?? (error ? 0 : 1),
                    },
                }
            );

    [Fact]
    public async Task AStepIsSentOnlyOnceItsInputWentOut()
    {
        var inputs = new InputTally();
        var sent = new List<object>();

        await Assert.ThrowsAsync<CoreCallException>(() =>
            LayoutSteps.Sending(
                inputs,
                sent.Add,
                "Properties",
                () =>
                    inputs.Invoke(
                        Click("Point is not on the target window (off-window or covered); no input sent.", true)
                    )
            )
        );
        Assert.Empty(sent);

        await LayoutSteps.Sending(inputs, sent.Add, "TOM Explorer", () => inputs.Invoke(Click("Clicked")));
        Assert.Equal(["TOM Explorer"], sent);
    }

    [Fact]
    public async Task AStepWhoseCallIsStillPendingIsAttempted()
    {
        // Core has not counted the pattern call yet, but it may still reach TE3.
        var inputs = new InputTally();
        var sent = new List<object>();

        var error = await Assert.ThrowsAsync<CoreCallException>(() =>
            LayoutSteps.Sending(
                inputs,
                sent.Add,
                "Properties",
                () =>
                    inputs.Invoke(
                        () =>
                            Task.FromResult(
                                new FlaUI.Mcp.McpToolResult
                                {
                                    IsError = true,
                                    Content = [new() { Text = "Not sent yet." }],
                                    Outcome = new FlaUI.Mcp.Core.ToolOutcome("timed_out_pending", "pending")
                                    {
                                        InputsSent = 0,
                                    },
                                }
                            ),
                        tool: "windows_click"
                    )
            )
        );

        Assert.Equal(0, inputs.Sent);
        Assert.Equal(["Properties"], sent);
        Assert.Equal("timed_out_pending", Te3Layout.Failure(error, inputs.Sent).Outcome?.Dispatch);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(2, true)]
    public void ALayoutFailureSaysWhetherInputWasSent(int inputsSent, bool dispatched)
    {
        var result = Te3Layout.Failure(new InvalidOperationException("Pane is gone"), inputsSent);
        var reply = System.Text.Json.JsonDocument.Parse(result.Content[0].Text!).RootElement;

        Assert.Contains("Pane is gone", reply.GetProperty("error").GetString());
        Assert.False(reply.TryGetProperty("actionDispatched", out _));
        Assert.False(reply.TryGetProperty("inputsSent", out _));
        Assert.Equal(dispatched ? "partial" : "not-dispatched", result.Outcome?.Dispatch);
        Assert.Equal(inputsSent, result.Outcome?.InputsSent);
    }

    [Fact]
    public async Task ACancelledLayoutCallStillSaysWhatItSent()
    {
        var result = await Te3Layout.Respond(async inputs =>
        {
            await inputs.Invoke(Click("Clicked Close"), tool: "windows_click");
            throw new OperationCanceledException("stopped");
        });

        Assert.True(result.IsError);
        Assert.Equal("partial", result.Outcome?.Dispatch);
        Assert.Equal(1, result.Outcome?.InputsSent);
    }

    [Fact]
    public async Task EachLayoutCallCountsOnlyItsOwnInputs()
    {
        var first = new TaskCompletionSource();
        var slow = Te3Layout.Respond(async inputs =>
        {
            await inputs.Invoke(Click("Clicked"), tool: "windows_click");
            await first.Task;
            throw new InvalidOperationException("Pane is gone");
        });

        var quick = await Te3Layout.Respond(_ => throw new InvalidOperationException("No TE3 window"));
        first.SetResult();

        Assert.Equal(0, quick.Outcome?.InputsSent);
        Assert.Equal(1, (await slow).Outcome?.InputsSent);
    }

    [Fact]
    public async Task AResizeDragRefusedMidDragCountsAsSent()
    {
        // windows_mouse drags with MoveTo, Down, several MoveTo and Up: a grant refusal after the button went down
        // is a partial call, and Core's count says the splitter may have moved.
        var inputs = new InputTally();
        var sent = new List<object>();

        await Assert.ThrowsAsync<CoreCallException>(() =>
            LayoutSteps.Sending(
                inputs,
                sent.Add,
                "Properties",
                () =>
                    inputs.Invoke(
                        Click(
                            "Desktop input needs the user's approval: keyboard or mouse input from the user was observed; ask them before resuming.",
                            true,
                            inputsSent: 2
                        ),
                        tool: "windows_mouse"
                    )
            )
        );

        Assert.True(inputs.Sent >= 1);
        Assert.Equal(["Properties"], sent);
        Assert.Equal(
            "partial",
            Te3Layout.Failure(new InvalidOperationException("drag refused"), inputs.Sent).Outcome?.Dispatch
        );
    }
}
