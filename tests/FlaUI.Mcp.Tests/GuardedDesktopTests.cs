using System.Diagnostics;
using FlaUI.Mcp;
using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>The public guarded calls product code uses instead of raw process starts, pattern calls and clipboard writes.</summary>
[Collection(InputLeaseCollection.Name)]
public sealed class GuardedDesktopTests
{
    private static int Counted(Action action)
    {
        var operation = new OperationContext();
        OperationContext.Current.Value = operation;
        try
        {
            action();
        }
        finally
        {
            OperationContext.Current.Value = null;
        }

        return operation.InputsSent;
    }

    private static T Denied<T>(Func<T> run)
    {
        MutationGuard.BeforeMutation.Value = () => throw new InvalidOperationException("mutation denied");
        try
        {
            return run();
        }
        finally
        {
            MutationGuard.BeforeMutation.Value = null;
        }
    }

    [Fact]
    public void AProcessStartGoesThroughTheDesktopAndCounts()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();

        Assert.Equal(1, Counted(() => GuardedDesktop.StartProcess(new ProcessStartInfo("app.exe"))));
        Assert.Equal(1, desktop.StartRequests);
    }

    [Fact]
    public async Task AStartThatStartsNothingCountsNothing()
    {
        using var offlineDesktop = new FakeDesktop { StartsNothing = true }.Install();
        var registry = new ToolRegistry();
        registry.RegisterTool(new StartsTool());

        var result = await registry.ExecuteToolAsync("starts", null);

        Assert.Equal(0, result.Outcome!.InputsSent);
        Assert.Equal("not-dispatched", result.Outcome.Dispatch);
    }

    [Fact]
    public void AProcessStartChecksPermissionFirst()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();

        Denied(() =>
            Assert.Throws<InvalidOperationException>(() => GuardedDesktop.StartProcess(new ProcessStartInfo("app.exe")))
        );

        Assert.Equal(0, desktop.StartRequests);
    }

    [Fact]
    public void APatternCallIsValidatedCountedAndRunModalAware()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        var order = new List<string>();
        PatternCallResult result = null!;

        var counted = Counted(() =>
            result = GuardedDesktop.CallPattern(
                FakeDesktop.TargetPid,
                "Expand on 'w1e1'",
                () => order.Add("call"),
                new PendingInvokeTracker(),
                () => order.Add("validate")
            )
        );

        Assert.Equal(PatternCallOutcome.Completed, result.Outcome);
        Assert.Equal(["validate", "call"], order);
        Assert.Equal(1, counted);
        Assert.Equal(1, desktop.PatternCalls);
    }

    [Fact]
    public void APatternCallIsRefusedWhileTheProviderIsBlocked()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        var pending = new PendingInvokeTracker(_ => 1);
        pending.Begin(FakeDesktop.TargetPid, "Invoke on 'Open...'");

        Assert.Throws<ProviderBlockedException>(() =>
            GuardedDesktop.CallPattern(FakeDesktop.TargetPid, "Expand on 'w1e1'", () => { }, pending)
        );

        Assert.Equal(0, desktop.PatternCalls);
    }

    [Fact]
    public void APatternCallIsRefusedForAHigherIntegrityProcess()
    {
        var desktop = new FakeDesktop { TargetIntegrity = new(ProcessIntegrity.High, false) };
        using var installed = desktop.Install();

        var error = Assert.Throws<InputRefusedException>(() =>
            GuardedDesktop.CallPattern(FakeDesktop.TargetPid, "Expand on 'w1e1'", () => { }, new PendingInvokeTracker())
        );

        Assert.Equal("integrity-blocked", error.Refusal.Code);
        Assert.Equal(0, desktop.PatternCalls);
    }

    [Theory]
    [InlineData(false, true, 1)]
    [InlineData(true, false, 0)]
    public void AClipboardWriteCountsOnlyWhenItLands(bool busy, bool written, int inputs)
    {
        var desktop = new FakeDesktop { ClipboardBusy = busy };
        using var installed = desktop.Install();
        var result = false;

        Assert.Equal(inputs, Counted(() => result = GuardedDesktop.SetClipboardText("abc")));
        Assert.Equal(written, result);
    }

    [Fact]
    public void AGuardWritesTheClipboardUnderItsOwnLease()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();

        var counted = Counted(() =>
        {
            using var input = new GuardedInput(FakeDesktop.Target);
            Assert.True(input.SetClipboardText("abc"));
            using var lease = input.PreserveAndSetClipboardText("def");
        });

        Assert.Equal(2, counted);
        Assert.Equal("def", desktop.ClipboardWritten);
    }

    private sealed class StartsTool : ToolBase
    {
        public override string Name => "starts";
        public override string Description => "no desktop";
        public override object InputSchema => new { };

        public override Task<McpToolResult> ExecuteAsync(System.Text.Json.JsonElement? arguments) =>
            Task.FromResult(
                GuardedDesktop.StartProcess(new ProcessStartInfo("app.exe")) is null
                    ? ErrorResult("Nothing was started.")
                    : TextResult("started")
            );
    }
}
