using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.CompilerServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>Tests written to kill mutants that survived in the guard files; see docs/mutation.md.</summary>
[Collection(InputLeaseCollection.Name)]
public sealed class GuardMutationTests
{
    private const nint ModalHwnd = 300;

    private static AutomationElement Element() =>
        (AutomationElement)RuntimeHelpers.GetUninitializedObject(typeof(AutomationElement));

    private static void AssertLeaseReleased()
    {
        using var lease = GuardedInput.AcquireLease();
    }

    [Fact]
    public void ARefusalAfterActivationReleasesTheLease()
    {
        var desktop = new FakeDesktop
        {
            ForegroundWindow = FakeDesktop.OtherHwnd,
            KeyboardFocus = false,
            ElementTakesFocus = false,
        };
        using var installed = desktop.Install();

        Assert.Throws<InvalidOperationException>(() => new GuardedInput(FakeDesktop.Target, Element(), true));

        AssertLeaseReleased();
    }

    [Fact]
    public void ARefusalAfterTheControlGainedFocusSaysSo()
    {
        // The control takes focus, then the disabled target fails verification.
        var desktop = new FakeDesktop { KeyboardFocus = false, TargetEnabled = false };
        using var installed = desktop.Install();

        var error = Assert.Throws<InvalidOperationException>(() => new GuardedInput(FakeDesktop.Target, Element()));

        Assert.EndsWith("Before this, the control was focused.", error.Message);
    }

    [Fact]
    public void ARefusalWhenNothingChangedKeepsItsText()
    {
        var desktop = new FakeDesktop { TargetEnabled = false };
        using var installed = desktop.Install();

        var error = Assert.Throws<InvalidOperationException>(() => new GuardedInput(FakeDesktop.Target, Element()));

        Assert.Equal("Input target/focus changed or is disabled; no further input sent.", error.Message);
    }

    [Fact]
    public void AnEnabledTargetIsActivatedItselfEvenWithAnOwnedWindow()
    {
        var desktop = new FakeDesktop { ForegroundWindow = FakeDesktop.OtherHwnd, ModalHwnd = ModalHwnd };
        using var installed = desktop.Install();

        new GuardedInput(FakeDesktop.Target).Dispose();

        Assert.Equal(FakeDesktop.TargetHwnd, desktop.ForegroundWindow);
    }

    [Theory]
    [InlineData("no input sent.", "no keys or clicks were sent, but x first.")]
    [InlineData("Refused. No input sent.", "Refused. No keys or clicks were sent, but x first.")]
    [InlineData("Refused; no input sent here.", "Refused; no keys or clicks were sent, but x first here.")]
    [InlineData("Focus changed; no further input sent.", "Focus changed; no further input sent. Before this, x.")]
    public void AFocusChangeIsWrittenIntoTheRefusal(string message, string expected)
    {
        Assert.Equal(expected, GuardedInput.NoteFocusChange(message, "x"));
    }

    [Fact]
    public void ABusyLeaseSaysNothingWasSent()
    {
        using var held = GuardedInput.AcquireLease();

        var error = Assert.Throws<AggregateException>(() => Task.Run(GuardedInput.AcquireLease).Wait());

        Assert.Equal("Desktop input is busy; no input sent.", error.InnerException!.Message);
    }

    [Fact]
    public void AWindowWithoutAHandleHasNoOwner()
    {
        var error = Assert.Throws<InvalidOperationException>(() => InputTarget.Capture(0, FakeDesktop.TargetPid));

        Assert.Equal("Cannot establish window ownership.", error.Message);
    }

    [Fact]
    public void ADeniedForegroundProcessIsNamed()
    {
        var policy = new ProcessPolicy(["some-app-that-is-not-running"]);
        var self = Process.GetCurrentProcess().ProcessName;

        var named = Assert.Throws<InvalidOperationException>(() =>
            GuardedInput.ForegroundTarget(policy, () => new InputTarget(1, Environment.ProcessId, 0))
        );
        var unnamed = Assert.Throws<InvalidOperationException>(() =>
            GuardedInput.ForegroundTarget(policy, () => new InputTarget(1, FakeDesktop.OtherPid, 0))
        );

        Assert.StartsWith($"The foreground window's process '{self}'", named.Message);
        Assert.StartsWith("The foreground window's process 'unknown'", unnamed.Message);
    }

    [Fact]
    public void ActivationRefusalsNameTheTargetAndWhatToDoNext()
    {
        var unavailable = GuardedInput.DescribeActivationFailure(42, 0, 0, null);
        var sameProcess = GuardedInput.DescribeActivationFailure(42, 7, 42, "app");

        Assert.Equal(42, unavailable.Target!.Pid);
        Assert.EndsWith(
            "No input sent. Observe again; unattended runs need an unlocked interactive session.",
            unavailable.Message
        );
        Assert.EndsWith(
            "No input sent. Focus the intended target window, then observe before retrying.",
            sameProcess.Message
        );
    }

    [Theory]
    [InlineData(true, false, "'unknown' (PID 9) runs at high integrity with UIAccess, above this server (medium)")]
    [InlineData(false, true, "'unknown' (PID 9) runs at high integrity, above this server (medium with UIAccess)")]
    public void IntegrityRefusalsDescribeBothSides(bool targetUiAccess, bool selfUiAccess, string reason)
    {
        var refusal = ProcessIntegrity.DescribeBlocked(
            new(ProcessIntegrity.Medium, selfUiAccess),
            new(ProcessIntegrity.High, targetUiAccess),
            9,
            null
        )!;

        Assert.StartsWith($"integrity-blocked: {reason}. ", refusal.Message);
    }

    [Fact]
    public void AnUnreadableTargetIsRefusedByName()
    {
        var refusal = ProcessIntegrity.DescribeBlocked(new(ProcessIntegrity.Medium, false), null, 9, "app")!;

        Assert.StartsWith(
            "integrity-blocked: the integrity level of 'app' (PID 9) cannot be read, so it may be above this server (medium). ",
            refusal.Message
        );
    }

    [Fact]
    public void AClosedWindowThatStillExistsIsReportedAsOpen()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();

        Assert.True(WindowMutation.Close(FakeDesktop.Target, FakeDesktop.TargetHwnd, "w1"));
        Assert.Equal(1, desktop.CloseRequests);
    }

    [Fact]
    public void FocusOfAStaleTargetIsRefused()
    {
        var desktop = new FakeDesktop
        {
            ForegroundWindow = FakeDesktop.OtherHwnd,
            OnEnsureAlive = () => throw new InvalidOperationException("Stale process/window identity."),
        };
        using var installed = desktop.Install();

        Assert.Throws<InvalidOperationException>(() =>
            WindowMutation.Focus(FakeDesktop.Target, FakeDesktop.TargetHwnd)
        );

        Assert.Equal(0, desktop.FocusAttempts);
    }

    [Fact]
    public void FocusCancelledDuringTheIdentityCheckIsNotSent()
    {
        var operation = new OperationContext();
        var desktop = new FakeDesktop
        {
            ForegroundWindow = FakeDesktop.OtherHwnd,
            OnEnsureAlive = operation.Stop.Cancel,
        };
        using var installed = desktop.Install();
        OperationContext.Current.Value = operation;
        try
        {
            Assert.Throws<OperationCanceledException>(() =>
                WindowMutation.Focus(FakeDesktop.Target, FakeDesktop.TargetHwnd)
            );
        }
        finally
        {
            OperationContext.Current.Value = null;
        }

        Assert.Equal(0, desktop.FocusAttempts);
    }

    [Fact]
    public void AProcessStartChecksMutationPermission()
    {
        using var offlineDesktop = new FakeDesktop { StartFails = true }.Install();
        var missing = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}", "missing.exe");
        MutationGuard.BeforeMutation.Value = () => throw new InvalidOperationException("mutation denied");
        try
        {
            var error = Assert.Throws<InvalidOperationException>(() =>
                WindowMutation.Start(new ProcessStartInfo(missing) { UseShellExecute = false })
            );
            Assert.Equal("mutation denied", error.Message);
        }
        finally
        {
            MutationGuard.BeforeMutation.Value = null;
        }

        Assert.Throws<Win32Exception>(() =>
            WindowMutation.Start(new ProcessStartInfo(missing) { UseShellExecute = false })
        );
    }

    [Theory]
    [InlineData(true, "The element has no on-screen bounds; no input sent.")]
    [InlineData(false, "The element reports no clickable point; no input sent.")]
    public void ClickPointRefusalsSayWhatIsMissing(bool emptyBounds, string start)
    {
        var geometry = new ClickGeometry(
            false,
            emptyBounds ? new Point(5, 5) : null,
            emptyBounds ? Rectangle.Empty : new Rectangle(0, 0, 10, 10),
            []
        );

        var error = Assert.Throws<InputRefusedException>(() => ClickPoint.Choose(geometry));

        Assert.StartsWith(start, error.Message);
    }

    [Fact]
    public void GuardedInputRevalidatesTheTargetIdentity()
    {
        var desktop = new FakeDesktop
        {
            ForegroundWindow = FakeDesktop.OtherHwnd,
            OnEnsureAlive = () => throw new InvalidOperationException("Stale process/window identity."),
        };
        using var installed = desktop.Install();

        Assert.Throws<InvalidOperationException>(() => new GuardedInput(FakeDesktop.Target));

        Assert.Equal(0, desktop.FocusAttempts);
        AssertLeaseReleased();
    }

    [Fact]
    public void GuardedInputChecksPermissionBeforeActivating()
    {
        var desktop = new FakeDesktop { ForegroundWindow = FakeDesktop.OtherHwnd };
        using var installed = desktop.Install();
        MutationGuard.BeforeMutation.Value = () => throw new InvalidOperationException("mutation denied");
        try
        {
            Assert.Throws<InvalidOperationException>(() => new GuardedInput(FakeDesktop.Target));
        }
        finally
        {
            MutationGuard.BeforeMutation.Value = null;
        }

        Assert.Equal(0, desktop.FocusAttempts);
    }

    [Fact]
    public void AnActiveTargetIsNotFocusedAgain()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();

        new GuardedInput(FakeDesktop.Target).Dispose();

        Assert.Equal(0, desktop.FocusAttempts);
    }

    [Fact]
    public void EachInputRechecksPermission()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        using var input = new GuardedInput(FakeDesktop.Target);
        MutationGuard.BeforeMutation.Value = () => throw new InvalidOperationException("mutation denied");
        try
        {
            Assert.Throws<InvalidOperationException>(() => input.Send(() => { }));
        }
        finally
        {
            MutationGuard.BeforeMutation.Value = null;
        }

        Assert.Equal(0, desktop.Dispatched);
    }

    [Fact]
    public void EachInputRechecksCancellation()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        var operation = new OperationContext();
        OperationContext.Current.Value = operation;
        try
        {
            using var input = new GuardedInput(FakeDesktop.Target);
            operation.Stop.Cancel();

            Assert.Throws<OperationCanceledException>(() => input.Send(() => { }));
        }
        finally
        {
            OperationContext.Current.Value = null;
        }

        Assert.Equal(0, desktop.Dispatched);
    }

    [Fact]
    public async Task WaitingForTheLeaseStopsWhenTheOperationIsCancelled()
    {
        var operation = new OperationContext();
        operation.Stop.Cancel();
        using var held = GuardedInput.AcquireLease();

        var waiting = Task.Run(() =>
        {
            OperationContext.Current.Value = operation;
            using var lease = GuardedInput.AcquireLease();
        });

        await Assert.ThrowsAsync<OperationCanceledException>(() => waiting);
    }

    [Fact]
    public void AnActivationRefusalNamesTheProcessInFront()
    {
        // PID 4 is the System process on every Windows machine.
        var desktop = new FakeDesktop
        {
            ForegroundWindow = FakeDesktop.OtherHwnd,
            ActivationAllowed = false,
            OtherProcessId = 4,
        };
        using var installed = desktop.Install();

        var error = Assert.Throws<InputRefusedException>(() => new GuardedInput(FakeDesktop.Target));

        Assert.Equal(ProcessPolicy.TryGetProcessName(4), error.Refusal.ForegroundProcess!.Name);
        Assert.NotNull(error.Refusal.ForegroundProcess.Name);
    }

    [Fact]
    public void ThisProcessIsReadWithoutUiAccess()
    {
        var self = ProcessIntegrity.Read(Environment.ProcessId)!;

        Assert.False(self.UiAccess);
        Assert.True(self.Level >= ProcessIntegrity.Medium);
    }
}
