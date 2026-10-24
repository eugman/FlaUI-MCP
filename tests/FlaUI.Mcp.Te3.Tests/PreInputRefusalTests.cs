using System.Text.Json;
using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>
/// A TE3 tool refused before any input (bad arguments, a precondition that did not hold) says so in its outcome as a
/// typed refusal with actionDispatched false, so the broker reads a pre-input refusal instead of a bare failure.
/// </summary>
public sealed class PreInputRefusalTests
{
    private const string Invalid = CoreCallException.InvalidArguments;
    private const string Precondition = CoreCallException.PreconditionFailed;

    private static JsonElement Args(object value) => JsonSerializer.SerializeToElement(value);

    private static void AssertRefusedBeforeInput(FlaUI.Mcp.McpToolResult result, string code)
    {
        Assert.True(result.IsError);
        Assert.Equal("not-dispatched", result.Outcome?.Dispatch);
        Assert.Equal(0, result.Outcome?.InputsSent);
        Assert.Equal(code, result.Outcome?.Failure?.Code);
        Assert.False(result.Outcome?.Failure?.ActionDispatched);
    }

    [Fact]
    public void AnArgumentFailureBeforeInputIsInvalidArguments()
    {
        var outcome = CoreFailure.Reply(new ArgumentException("destination is required."), inputsSent: 0);

        Assert.Equal(Invalid, outcome.Failure?.Code);
        Assert.Equal("destination is required.", outcome.Failure?.Message);
        Assert.False(outcome.Failure?.ActionDispatched);
    }

    [Fact]
    public void APreconditionFailureBeforeInputIsPreconditionFailed()
    {
        var outcome = CoreFailure.Reply(new InvalidOperationException("Preferences is open."), inputsSent: 0);

        Assert.Equal(Precondition, outcome.Failure?.Code);
        Assert.False(outcome.Failure?.ActionDispatched);
    }

    [Fact]
    public void AWrappedCauseDecidesTheCode()
    {
        var wrapped = new LayoutSteps.ProgressException("closing stopped", "closed", [], new ArgumentException("x"));

        Assert.Equal(Invalid, CoreFailure.Reply(wrapped, inputsSent: 0).Failure?.Code);
    }

    public static TheoryData<Exception> Untyped =>
        new() { new TimeoutException("UIA Timeout"), new OperationCanceledException(), new TaskCanceledException() };

    [Theory]
    [MemberData(nameof(Untyped))]
    public void ATimedOutOrCancelledReadStaysUntyped(Exception error) =>
        Assert.Null(CoreFailure.Reply(error, inputsSent: 0).Failure);

    [Fact]
    public void AFailureAfterInputIsNotTypedAsARefusal() =>
        Assert.Null(CoreFailure.Reply(new InvalidOperationException("not found"), inputsSent: 2).Failure);

    [Fact]
    public void ASettledCoreFailureWithoutARefusalIsTypedAsNoInput()
    {
        var failed = new CoreCallException(
            "windows_click",
            new FlaUI.Mcp.McpToolResult
            {
                IsError = true,
                Content = [new() { Type = "text", Text = "No element matched" }],
                Outcome = new ToolOutcome("not-dispatched", "not-tracked") { InputsSent = 0, Status = "failed" },
            }
        );

        var reply = CoreFailure.Reply(failed, inputsSent: 0);
        Assert.Equal(CoreCallException.ToolFailed, reply.Failure?.Code);
        Assert.False(reply.Failure?.ActionDispatched);
        Assert.Equal("not-dispatched", reply.Dispatch);
    }

    [Fact]
    public void ACoreFailureKnownToHaveDispatchedKeepsThatEvidenceAcrossAnOuterZeroTally()
    {
        var failed = new CoreCallException(
            "windows_click",
            new FlaUI.Mcp.McpToolResult
            {
                IsError = true,
                Content = [new() { Type = "text", Text = "Effect may have occurred" }],
                Outcome = new ToolOutcome("partial", "not-tracked")
                {
                    InputsSent = 0,
                    Status = "failed",
                    Failure = new InputRefusal("target-changed", "Effect may have occurred")
                    {
                        ActionDispatched = true,
                    },
                },
            }
        );

        var reply = CoreFailure.Reply(failed, inputsSent: 0);
        Assert.Equal("partial", reply.Dispatch);
        Assert.Equal(0, reply.InputsSent);
        Assert.True(reply.Failure?.ActionDispatched);
    }

    [Theory]
    [InlineData("partial")]
    [InlineData("completed")]
    public void ACoreDispatchWithoutARefusalIsNotReclassifiedAsNoInput(string dispatch)
    {
        var failed = new CoreCallException(
            "windows_click",
            new FlaUI.Mcp.McpToolResult
            {
                IsError = true,
                Content = [new() { Type = "text", Text = "The action failed" }],
                Outcome = new ToolOutcome(dispatch, "not-tracked") { InputsSent = 0, Status = "failed" },
            }
        );

        var reply = CoreFailure.Reply(failed, inputsSent: 0);
        Assert.Equal("partial", reply.Dispatch);
        Assert.Null(reply.Failure);
    }

    [Fact]
    public async Task NavigateWithoutADestinationIsInvalidArguments()
    {
        var result = await new Te3Companion(null!, Te3Companion.Navigate).ExecuteAsync(Args(new { }));

        AssertRefusedBeforeInput(result, Invalid);
    }

    [Fact]
    public async Task AProcessThatIsNotTe3IsInvalidArguments()
    {
        var result = await new Te3Companion(null!, Te3Companion.ModelState).ExecuteAsync(
            Args(new { processId = Environment.ProcessId })
        );

        AssertRefusedBeforeInput(result, Invalid);
    }

    [Theory]
    [InlineData(0, Precondition)]
    [InlineData(2, Invalid)]
    public void ResolvingTheProcessIsTypedByWhatWentWrong(int running, string code)
    {
        var error = Record.Exception(() =>
            Te3Companion.ResolveProcessId(Args(new { }), () => Enumerable.Range(100, running).ToArray())
        );

        AssertRefusedBeforeInput(Te3Companion.Failure("attach", "validate request", error!), code);
    }

    [Fact]
    public void AnotherTe3WindowIsAPreconditionFailure()
    {
        var main = new Win32WindowInfo(0x10, "Model - Tabular Editor 3", 7, true, false, false);
        var prompt = new Win32WindowInfo(0x40, "Save changes?", 7, true, false, false);

        var error = Record.Exception(() =>
            Te3Dialogs.RequireNoOtherWindow([main, prompt], main.Hwnd, "Preferences", _ => null)
        );

        AssertRefusedBeforeInput(Te3Companion.Failure("navigate", "navigate object", error!), Precondition);
    }

    [Fact]
    public void AMissingTe3WindowIsAPreconditionFailure()
    {
        var error = Record.Exception(() => Te3Companion.MainWindow([]));

        AssertRefusedBeforeInput(Te3Companion.Failure("attach", "validate request", error!), Precondition);
    }

    private static Te3Layout Layout(Te3Interaction interaction) =>
        new(null!, interaction, attach: (_, inputs) => new FakeSurface(inputs, "Properties"));

    [Theory]
    [InlineData("bogus", Invalid)]
    [InlineData("close", Invalid)]
    [InlineData("show-pane", Invalid)]
    [InlineData("reopen-pane", Precondition)]
    public async Task ALayoutCallStoppedBeforeInputIsTyped(string action, string code)
    {
        using var interaction = new Te3Interaction(() => 0);
        interaction.Begin("arrange TE3", userApproved: true);

        var result = await Layout(interaction)
            .ExecuteAsync(Args(action == "reopen-pane" ? new { action, title = "Properties" } : new { action }));

        AssertRefusedBeforeInput(result, code);
    }

    [Fact]
    public async Task ABeginWithoutApprovalIsInvalidArguments()
    {
        using var interaction = new Te3Interaction(() => 0);
        var begin = new Te3InteractionTool(interaction, Te3InteractionTool.Begin);

        var result = await begin.ExecuteAsync(Args(new { batch = "Open Preferences", userApproved = false }));

        AssertRefusedBeforeInput(result, Invalid);
        Assert.False(interaction.Active);
    }

    [Fact]
    public async Task ASecondBeginIsAPreconditionFailure()
    {
        using var interaction = new Te3Interaction(() => 0);
        var begin = new Te3InteractionTool(interaction, Te3InteractionTool.Begin);
        await begin.ExecuteAsync(Args(new { batch = "Open Preferences", userApproved = true }));

        var result = await begin.ExecuteAsync(Args(new { batch = "Again", userApproved = true }));

        AssertRefusedBeforeInput(result, Precondition);
    }

    private sealed class FailingHandler(Exception error) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromException<HttpResponseMessage>(error);
    }

    private static JsonElement PreflightArgs(string identityTool) =>
        Args(
            new
            {
                endpoint = "http://127.0.0.1:42100/",
                identityTool,
                identityProperty = "model.name",
                expectedIdentity = "scratch-model",
            }
        );

    [Fact]
    public async Task APreflightWithABadArgumentIsInvalidArguments()
    {
        var tool = new Te3NativePreflightTool(new HttpClient(new FailingHandler(new InvalidOperationException("x"))));

        var result = await tool.ExecuteAsync(PreflightArgs(identityTool: ""));

        AssertRefusedBeforeInput(result, Invalid);
    }

    [Fact]
    public async Task APreflightWhoseEndpointIsUnreachableIsAPreconditionFailure()
    {
        var tool = new Te3NativePreflightTool(
            new HttpClient(new FailingHandler(new HttpRequestException("connection refused")))
        );

        var result = await tool.ExecuteAsync(PreflightArgs("model_identity"));

        AssertRefusedBeforeInput(result, Precondition);
        Assert.Contains("connection refused", result.Content[0].Text);
    }
}
