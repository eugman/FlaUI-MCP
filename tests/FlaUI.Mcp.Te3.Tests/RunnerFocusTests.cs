using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>The runner's focus request for its TE3 window, on a window that is never shown and a fake desktop.</summary>
public sealed class RunnerFocusTests : IDisposable
{
    private readonly string journal = Path.Combine(
        Path.GetTempPath(),
        "fla_focus_test_" + Guid.NewGuid().ToString("N") + ".jsonl"
    );

    public void Dispose() => File.Delete(journal);

    [Fact]
    public async Task FocusingTheRunsWindowIsJournaledAndCountedAsInput()
    {
        using var host = new AutomationHost(ProcessPolicy.AllowAll);
        using var form = new System.Windows.Forms.Form();
        var handle = host.Sessions.RegisterNativeWindow(form.Handle, Environment.ProcessId);
        var desktop = new FakeDesktop { ForegroundWindow = FakeDesktop.OtherHwnd };
        var inputs = new InputTally();

        using (desktop.Install())
        {
            await RunExecutor.FocusOwnedWindow(host, handle, journal, inputs);
        }

        Assert.Equal(1, desktop.FocusRequests);
        Assert.Equal(1, inputs.Sent);
        Assert.Contains("te3_focus_window", File.ReadAllText(journal));
    }

    [Fact]
    public async Task FocusingAWindowAlreadyInFrontSendsNothing()
    {
        using var host = new AutomationHost(ProcessPolicy.AllowAll);
        using var form = new System.Windows.Forms.Form();
        var handle = host.Sessions.RegisterNativeWindow(form.Handle, Environment.ProcessId);
        var desktop = new FakeDesktop { ForegroundWindow = form.Handle };
        var inputs = new InputTally();

        using (desktop.Install())
        {
            await RunExecutor.FocusOwnedWindow(host, handle, journal, inputs);
        }

        Assert.Equal(0, inputs.Sent);
    }

    [Fact]
    public async Task AHeldProviderRefusesTheFocusBeforeAnythingIsSent()
    {
        // Core's native-handle focus never consults the pending tracker, so the runner checks it first.
        using var host = new AutomationHost(ProcessPolicy.AllowAll);
        using var form = new System.Windows.Forms.Form();
        var handle = host.Sessions.RegisterNativeWindow(form.Handle, Environment.ProcessId);
        var pending = host.Pending.Begin(Environment.ProcessId, "Invoke Save");
        var desktop = new FakeDesktop { ForegroundWindow = FakeDesktop.OtherHwnd };
        var inputs = new InputTally();

        using (desktop.Install())
        {
            var error = await Assert.ThrowsAsync<ProviderBlockedException>(() =>
                RunExecutor.FocusOwnedWindow(host, handle, journal, inputs)
            );
            Assert.Same(pending, error.Pending);
        }

        Assert.Equal(0, desktop.FocusRequests);
        Assert.Equal(0, inputs.Sent);
    }
}
