using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Mcp;
using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// inputsSent counts every change a call made to the desktop, and only those: focus changes, pattern
/// calls, clipboard writes and button releases included, no-ops and failed starts excluded.
/// </summary>
[Collection(InputLeaseCollection.Name)]
public sealed class DispatchAccountingTests
{
    private static JsonElement Args(object value) => JsonSerializer.SerializeToElement(value);

    private static AutomationElement Element() =>
        (AutomationElement)RuntimeHelpers.GetUninitializedObject(typeof(AutomationElement));

    /// <summary>Runs <paramref name="action"/> as an operation and returns how many inputs it counted.</summary>
    private static int Counted(Action action)
    {
        var operation = new OperationContext();
        OperationContext.Current.Value = operation;
        try
        {
            action();
        }
        catch (InvalidOperationException) { }
        finally
        {
            OperationContext.Current.Value = null;
        }

        return operation.InputsSent;
    }

    private static (SessionManager Sessions, string Handle) Window()
    {
        var sessions = new SessionManager(null, ProcessPolicy.AllowAll);
        var handle = sessions.PublishWindow(null, FakeDesktop.TargetHwnd, FakeDesktop.TargetPid, FakeDesktop.Target);
        return (sessions, handle);
    }

    // K1: activation and control focus change the desktop, so they count; a no-op does not.

    [Fact]
    public void ActivatingTheTargetCountsAsOneChange()
    {
        var desktop = new FakeDesktop { ForegroundWindow = FakeDesktop.OtherHwnd };
        using var installed = desktop.Install();

        Assert.Equal(1, Counted(() => new GuardedInput(FakeDesktop.Target).Dispose()));
    }

    [Fact]
    public void AnAlreadyActiveTargetCountsNothing()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();

        Assert.Equal(0, Counted(() => new GuardedInput(FakeDesktop.Target).Dispose()));
    }

    [Fact]
    public void ARefusedActivationCountsNothing()
    {
        var desktop = new FakeDesktop { ForegroundWindow = FakeDesktop.OtherHwnd, ActivationAllowed = false };
        using var installed = desktop.Install();

        Assert.Equal(0, Counted(() => new GuardedInput(FakeDesktop.Target).Dispose()));
    }

    [Fact]
    public void FocusingAControlThatLackedFocusCountsAsOneChange()
    {
        var desktop = new FakeDesktop { KeyboardFocus = false };
        using var installed = desktop.Install();

        Assert.Equal(1, Counted(() => new GuardedInput(FakeDesktop.Target, Element(), verifyFocus: true).Dispose()));
    }

    [Fact]
    public void FocusingAControlThatAlreadyHadFocusCountsNothing()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();

        Assert.Equal(0, Counted(() => new GuardedInput(FakeDesktop.Target, Element(), verifyFocus: true).Dispose()));
    }

    [Fact]
    public void ARefusalAfterActivationSaysTheWindowWasActivated()
    {
        var desktop = new FakeDesktop { ForegroundWindow = FakeDesktop.OtherHwnd, KeyboardFocus = false };
        desktop.ElementTakesFocus = false;
        using var installed = desktop.Install();

        var error = Assert.Throws<InvalidOperationException>(() =>
            new GuardedInput(FakeDesktop.Target, Element(), verifyFocus: true)
        );

        Assert.Contains("did not receive verified keyboard focus", error.Message);
        Assert.Contains("the target window was activated first", error.Message);
        Assert.DoesNotContain("no input sent", error.Message);
    }

    private sealed class DelegateTool(string name, Func<McpToolResult> run) : ToolBase
    {
        public override string Name => name;
        public override string Description => "no desktop";
        public override object InputSchema => new { };

        public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments) => Task.FromResult(run());
    }
}
