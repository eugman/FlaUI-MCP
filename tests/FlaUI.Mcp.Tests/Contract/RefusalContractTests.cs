using System.Text.Json;
using FlaUI.Mcp;
using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Tests.Contract;

/// <summary>
/// Refusals are one family, AutomationRefusal, apart from InvalidOperationException, so a poll that catches "not
/// ready yet" can no longer swallow a refusal and relabel it. Each carries a hyphenated code, how much input went
/// out first, and a disposition, and keeps its code through the registry.
/// </summary>
[Collection(InputLeaseCollection.Name)]
public sealed class RefusalContractTests
{
    private sealed class HostRefusal(string code, string message) : AutomationRefusal(code, message);

    [Fact]
    public void TheRefusalTypesAreAutomationRefusalsNotInvalidOperations()
    {
        Exception[] refusals =
        [
            new InputRefusedException(new InputRefusal("integrity-blocked", "x")),
            new ElementNotFoundException("No element matched"),
            new ProviderBlockedException(new PendingInvokeInfo(7, "Invoke", 0)),
        ];

        Assert.All(refusals, refusal => Assert.IsAssignableFrom<AutomationRefusal>(refusal));
        Assert.All(refusals, refusal => Assert.IsNotAssignableFrom<InvalidOperationException>(refusal));
        Assert.Equal("element-not-found", ((AutomationRefusal)refusals[1]).Code);
        Assert.Equal("provider-blocked", ((AutomationRefusal)refusals[2]).Code);
    }

    [Theory]
    [InlineData("focus_revoked", "focus-revoked")]
    [InlineData("approval_required", "approval-required")]
    [InlineData("integrity-blocked", "integrity-blocked")]
    public void CodesAreHyphenatedWhicheverSpellingAHostUses(string given, string canonical)
    {
        Assert.Equal(canonical, RefusalCodes.Canonical(given));
        Assert.Equal(canonical, InputRefusedException.Record(new InputRefusal(given, "x")).Refusal.Code);
        Assert.Equal(canonical, new HostRefusal(given, "x").Code);
    }

    [Theory]
    [InlineData(0, false, RefusalDisposition.NoInput)]
    [InlineData(2, false, RefusalDisposition.Partial)]
    [InlineData(0, true, RefusalDisposition.Partial)]
    public void TheDispositionFollowsWhatWentOut(int inputs, bool dispatched, RefusalDisposition disposition) =>
        Assert.Equal(
            disposition,
            new HostRefusal("x", "y") { InputsSent = inputs, ActionDispatched = dispatched }.Disposition
        );

    [Fact]
    public async Task AHostHooksRefusalKeepsItsCodeInTheOutcome()
    {
        using var installed = new FakeDesktop().Install();
        var registry = new ToolRegistry();
        registry.RegisterTool(new SendsOneKey());
        MutationGuard.BeforeMutation.Value = () => throw new HostRefusal("pause_gate", "The session is paused");
        try
        {
            var result = await registry.ExecuteToolAsync("sends-one-key", null);

            Assert.True(result.IsError);
            Assert.Equal("pause-gate", result.Outcome!.Failure!.Code);
            Assert.Equal("not-dispatched", result.Outcome.Dispatch);
        }
        finally
        {
            MutationGuard.BeforeMutation.Value = null;
        }
    }

    [Fact]
    public async Task AHookThatThrowsAnyOtherExceptionIsRecordedAsPermissionDenied()
    {
        using var installed = new FakeDesktop().Install();
        var registry = new ToolRegistry();
        registry.RegisterTool(new SendsOneKey());
        MutationGuard.BeforeMutation.Value = () => throw new InvalidOperationException("host says no");
        try
        {
            var result = await registry.ExecuteToolAsync("sends-one-key", null);

            Assert.Equal("permission-denied", result.Outcome!.Failure!.Code);
            Assert.Equal("host says no", result.Outcome.Failure.Message);
        }
        finally
        {
            MutationGuard.BeforeMutation.Value = null;
        }
    }

    [Fact]
    public async Task AFailureAfterInputReachesTheHostsPauseHook()
    {
        var desktop = new FakeDesktop { FocusLostAfter = 1 };
        using var installed = desktop.Install();
        var paused = new List<string>();
        var registry = new ToolRegistry { FailedAfterInput = (tool, _) => paused.Add(tool) };
        registry.RegisterTool(new SendsTwoKeys());

        var result = await registry.ExecuteToolAsync("sends-two-keys", null);

        Assert.Equal("partial", result.Outcome!.Dispatch);
        Assert.Equal(["sends-two-keys"], paused);
    }

    [Fact]
    public async Task AFailureBeforeInputDoesNotPause()
    {
        using var installed = new FakeDesktop
        {
            ActivationAllowed = false,
            ForegroundWindow = FakeDesktop.OtherHwnd,
        }.Install();
        var paused = new List<string>();
        var registry = new ToolRegistry { FailedAfterInput = (tool, _) => paused.Add(tool) };
        registry.RegisterTool(new SendsOneKey());

        var result = await registry.ExecuteToolAsync("sends-one-key", null);

        Assert.True(result.IsError);
        Assert.Empty(paused);
    }

    private sealed class SendsOneKey : ToolBase
    {
        public override string Name => "sends-one-key";
        public override string Description => "no desktop";
        public override object InputSchema => new { };

        public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
        {
            try
            {
                using var input = new GuardedInput(FakeDesktop.Target);
                input.Send(() => { });
                return Task.FromResult(TextResult("sent"));
            }
            catch (Exception error)
            {
                return Task.FromResult(ErrorResult($"Failed: {error.Message}"));
            }
        }
    }

    private sealed class SendsTwoKeys : ToolBase
    {
        public override string Name => "sends-two-keys";
        public override string Description => "no desktop";
        public override object InputSchema => new { };

        public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
        {
            try
            {
                using var input = new GuardedInput(FakeDesktop.Target);
                input.Send(() => { });
                input.Send(() => { });
                return Task.FromResult(TextResult("sent"));
            }
            catch (Exception error)
            {
                return Task.FromResult(ErrorResult($"Failed: {error.Message}"));
            }
        }
    }
}
