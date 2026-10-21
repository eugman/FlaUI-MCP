using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Mcp;
using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>Tests written to kill mutants that survived in the send-keys, type, fill and batch tools.</summary>
[Collection(InputLeaseCollection.Name)]
public sealed class KeyboardMutationTests
{
    private const string LineBreak = "\n";

    private static JsonElement Args(object value) => JsonSerializer.SerializeToElement(value);

    private static JsonElement[] Rows(params object[] rows) => rows.Select(row => Args(row)).ToArray();

    private static AutomationElement Element() =>
        (AutomationElement)RuntimeHelpers.GetUninitializedObject(typeof(AutomationElement));

    private static (SessionManager Sessions, string Handle) Window()
    {
        var sessions = new SessionManager(null, ProcessPolicy.AllowAll);
        var handle = sessions.PublishWindow(null, FakeDesktop.TargetHwnd, FakeDesktop.TargetPid, FakeDesktop.Target);
        return (sessions, handle);
    }

    private static McpToolResult Step(string text, ToolOutcome outcome) =>
        new() { Content = [new() { Type = "text", Text = text }], Outcome = outcome };

    [Fact]
    public async Task BatchStepsAreNumberedFromOneWithTheirOwnOutcomes()
    {
        var rows = Rows(
            new { action = "wait", ms = 0 },
            new { action = "click", @ref = "w1e1" },
            new { action = "zap" }
        );

        var result = await BatchTool.ExecuteRows(
            rows,
            (_, _) => Task.FromResult(Step("clicked", new("completed", "returned") { InputsSent = 1 })),
            stopOnError: false
        );

        Assert.Equal("1. wait: Waited 0ms\n2. click: clicked\n3. ERROR: Unknown action: zap", result.Content[0].Text);
        var steps = result.Outcome!.Steps!;
        Assert.Equal([1, 2, 3], steps.Select(s => s.Index));
        Assert.Equal(("completed", "returned"), (steps[0].Outcome!.Dispatch, steps[0].Outcome!.Provider));
        Assert.Equal(("not-dispatched", "unknown"), (steps[2].Outcome!.Dispatch, steps[2].Outcome!.Provider));
    }

    [Fact]
    public async Task ABatchCountsEveryStepsInputs()
    {
        var sent = new Queue<int>([1, 2]);
        var rows = Rows(new { action = "click", @ref = "w1e1" }, new { action = "click", @ref = "w1e2" });

        var result = await BatchTool.ExecuteRows(
            rows,
            (_, _) => Task.FromResult(Step("ok", new("completed", "returned") { InputsSent = sent.Dequeue() }))
        );

        Assert.Equal(3, result.Outcome!.InputsSent);
        Assert.Equal("returned", result.Outcome.Provider);
        Assert.Null(result.Outcome.Status);
    }

    [Fact]
    public async Task ABatchStopsAtAPendingStep()
    {
        var calls = 0;
        var rows = Rows(new { action = "click", @ref = "w1e1" }, new { action = "click", @ref = "w1e2" });

        var result = await BatchTool.ExecuteRows(
            rows,
            (_, _) =>
            {
                calls++;
                return Task.FromResult(Step("Invoked OK", new("completed", "pending")));
            }
        );

        Assert.Equal(1, calls);
        Assert.EndsWith(
            "Stopped at action 1: operation still pending; inspect state before continuing, do not replay.",
            result.Content[0].Text
        );
    }

    [Fact]
    public async Task ABatchWaitReallyWaitsAndStopsWhenCancelled()
    {
        var operation = new OperationContext();
        OperationContext.Current.Value = operation;
        try
        {
            var watch = Stopwatch.StartNew();
            await BatchTool.ExecuteRows(
                Rows(new { action = "wait", ms = 200 }),
                (_, _) => throw new InvalidOperationException()
            );
            Assert.True(watch.ElapsedMilliseconds >= 150);

            operation.Stop.CancelAfter(100);
            watch.Restart();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                BatchTool.ExecuteRows(
                    Rows(new { action = "wait", ms = 5000 }),
                    (_, _) => throw new InvalidOperationException()
                )
            );
            Assert.True(watch.ElapsedMilliseconds < 3000);
        }
        finally
        {
            OperationContext.Current.Value = null;
        }
    }

    [Fact]
    public async Task StrictFocusWithoutARefIsRefusedByBothKeyboardTools()
    {
        using var offlineDesktop = new FakeDesktop().Install();
        var keys = await new SendKeysTool(new ElementRegistry()).ExecuteAsync(
            Args(new { chord = "Enter", verifyFocus = true })
        );
        var type = await new TypeTool(new ElementRegistry()).ExecuteAsync(Args(new { text = "x", verifyFocus = true }));

        Assert.Equal("verifyFocus requires an element ref; no input sent.", keys.Content[0].Text);
        Assert.Equal("verifyFocus requires an element ref; no input sent.", type.Content[0].Text);
    }

    [Fact]
    public async Task StrictFocusWithARefIsNotRefusedForTheMissingRef()
    {
        using var offlineDesktop = new FakeDesktop().Install();
        var refs = new ElementRegistry();
        var reference = refs.Register("w1", Element());

        var keys = await new SendKeysTool(refs).ExecuteAsync(
            Args(
                new
                {
                    @ref = reference,
                    chord = "Enter",
                    verifyFocus = true,
                }
            )
        );
        var type = await new TypeTool(refs).ExecuteAsync(
            Args(
                new
                {
                    @ref = reference,
                    text = "x",
                    verifyFocus = true,
                }
            )
        );

        // Without a process identity the ref cannot take input; that, not the ref, is the refusal.
        Assert.Contains("establish input ownership", keys.Content[0].Text);
        Assert.Contains("establish input ownership", type.Content[0].Text);
    }

    [Fact]
    public async Task AnEmptyKeyListIsNoKeysAtAll()
    {
        using var offlineDesktop = new FakeDesktop().Install();
        var result = await new SendKeysTool(new ElementRegistry()).ExecuteAsync(
            Args(new { keys = Array.Empty<string>() })
        );

        Assert.Equal("Provide either chord or keys.", result.Content[0].Text);
    }

    [Fact]
    public async Task AnUnknownRefIsReportedBeforeAnyInputCheck()
    {
        using var offlineDesktop = new FakeDesktop().Install();
        var keys = await new SendKeysTool(new ElementRegistry()).ExecuteAsync(
            Args(new { @ref = "w1e9", chord = "Enter" })
        );
        var type = await new TypeTool(new ElementRegistry()).ExecuteAsync(Args(new { @ref = "w1e9", text = "x" }));

        Assert.Contains("Element not found: w1e9.", keys.Content[0].Text);
        Assert.Contains("Element not found: w1e9.", type.Content[0].Text);
    }

    [Fact]
    public async Task AFocusClassMismatchStopsBeforeTheFirstKey()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        var (sessions, handle) = Window();
        using var _ = sessions;

        var result = await new SendKeysTool(new ElementRegistry(), sessions: sessions).ExecuteAsync(
            Args(
                new
                {
                    handle,
                    chord = "Enter",
                    expectFocusClass = "NoControlHasThisClass",
                }
            )
        );

        Assert.True(result.IsError);
        Assert.Equal(0, desktop.Dispatched);
    }

    [Fact]
    public async Task AKeySequenceReportsEveryKeyAndWhereFocusWas()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        var (sessions, handle) = Window();
        using var _ = sessions;

        var result = await new SendKeysTool(new ElementRegistry(), sessions: sessions).ExecuteAsync(
            Args(new { handle, keys = new[] { "Tab", "Tab" } })
        );

        // Where focus went afterwards depends on the live desktop, so only the dispatch note is checked.
        Assert.StartsWith(
            "Sent key sequence [Tab, Tab] to focused element (focus at dispatch: ",
            result.Content[0].Text
        );
    }

    [Fact]
    public async Task AChordThatFailsAfterInputWasAttemptedSaysItMayHavePartlyRun()
    {
        var desktop = new FakeDesktop { FocusLostAfter = 1 };
        using var installed = desktop.Install();
        var (sessions, handle) = Window();
        using var _ = sessions;

        var result = await new SendKeysTool(new ElementRegistry(), sessions: sessions).ExecuteAsync(
            Args(new { handle, keys = new[] { "Tab", "Tab" } })
        );

        Assert.EndsWith(
            "Completed 1 chord(s). A failing chord may have partially executed; inspect state before replaying input.",
            result.Content[0].Text
        );
    }

    [Fact]
    public void TheWindowsKeyIsRefusedUnderAnAllowlist()
    {
        using var offlineDesktop = new FakeDesktop().Install();
        var tool = new SendKeysTool(new ElementRegistry(), processPolicy: new ProcessPolicy(["contoso"]));

        var result = tool.ExecuteAsync(Args(new { chord = "Win+R" })).GetAwaiter().GetResult();

        Assert.Contains(
            "The Windows key is disabled while the app allowlist is active, because it opens system UI outside the allowed apps.",
            result.Content[0].Text
        );
    }

    [Fact]
    public void AFocusWithoutAClassNameIsDescribedAsUnknown()
    {
        var focus = new FocusedControl(1, "", 2, "Editor", Environment.ProcessId);

        Assert.Equal(
            " (focus: class=unknown class in \"Editor\")",
            SendKeysTool.DescribeFocus(focus, ProcessPolicy.AllowAll)
        );
    }

    [Fact]
    public async Task TypingWithoutSubmitSendsOnlyTheText()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        var (sessions, handle) = Window();
        using var _ = sessions;

        var result = await new TypeTool(new ElementRegistry(), sessions: sessions).ExecuteAsync(
            Args(new { handle, text = "a" })
        );

        Assert.Equal("Typed \"a\" into focused element", result.Content[0].Text);
        Assert.Equal(1, desktop.Dispatched);
    }

    [Fact]
    public async Task ASubmitThatFailsAfterEveryLineDoesNotBlameALine()
    {
        var desktop = new FakeDesktop { FocusLostAfter = 1 };
        using var installed = desktop.Install();
        var (sessions, handle) = Window();
        using var _ = sessions;

        var result = await new TypeTool(new ElementRegistry(), sessions: sessions).ExecuteAsync(
            Args(
                new
                {
                    handle,
                    text = "a",
                    submit = true,
                }
            )
        );

        Assert.EndsWith("Typed 1 of 1 line(s).", result.Content[0].Text);
    }

    [Fact]
    public void AKeyboardFillRechecksTheTargetWhileWaitingForTheText()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        using var input = new GuardedInput(FakeDesktop.Target);
        var checks = 0;

        var error = Assert.Throws<InvalidOperationException>(() =>
            FillTool.FillByKeyboard(
                input,
                "new",
                "field",
                () => new("old", "Value"),
                () =>
                {
                    if (++checks > 1)
                    {
                        throw new InvalidOperationException("stale ref");
                    }
                }
            )
        );

        Assert.Equal("stale ref", error.Message);
    }

    [Theory]
    [InlineData("Edit", "; warning: it now reads \"b\", so the value may not have applied.")]
    [InlineData(
        "ComboBox",
        "; warning: it now reads \"b\", so the value may not have applied. For a ComboBox, fill its Edit child instead."
    )]
    public void AFillThatDidNotApplySaysWhatTheControlReads(string type, string expected)
    {
        var control = type == "Edit" ? ControlType.Edit : ControlType.ComboBox;

        Assert.Equal(expected, FillTool.FillMismatch("a", "b", control));
    }

    [Fact]
    public async Task ABatchTraceKeepsEveryLineOfAStepsReply()
    {
        var result = await BatchTool.ExecuteRows(
            Rows(new { action = "click", @ref = "w1e1" }),
            (_, _) =>
                Task.FromResult(
                    new McpToolResult
                    {
                        Content = [new() { Type = "text", Text = "first" }, new() { Type = "text", Text = "second" }],
                    }
                )
        );

        Assert.Equal(string.Join(LineBreak, "1. click: first", "second"), result.Content[0].Text);
    }
}
