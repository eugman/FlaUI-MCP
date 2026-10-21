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

    private long userInput;

    public void Dispose() => File.Delete(journal);

    // The run's grant, begun as the run command begins it, with the user's input counted by a fake.
    private Te3Interaction Approved()
    {
        var interaction = new Te3Interaction(() => Interlocked.Read(ref userInput));
        interaction.Begin("run model-open", userApproved: true);
        return interaction;
    }

    [Fact]
    public async Task FocusingTheRunsWindowUnderTheRunsGrantIsJournaledAndCountedAsInput()
    {
        using var host = new AutomationHost(ProcessPolicy.AllowAll);
        using var form = new System.Windows.Forms.Form();
        var handle = host.Sessions.RegisterNativeWindow(form.Handle, Environment.ProcessId);
        var desktop = new FakeDesktop { ForegroundWindow = FakeDesktop.OtherHwnd };
        var inputs = new InputTally();
        using var interaction = Approved();

        using (desktop.Install())
        using (interaction.Guard())
        {
            await RunExecutor.FocusOwnedWindow(host, handle, journal, inputs);
        }

        Assert.Equal(1, desktop.FocusRequests);
        Assert.Equal(1, inputs.Sent);
        Assert.Contains("te3_focus_window", File.ReadAllText(journal));
    }

    [Fact]
    public async Task FocusingTheRunsWindowWithoutTheRunsGrantSendsNothing()
    {
        // Core's hook allows everything while nothing sets it, so the run checks for its own grant first.
        using var host = new AutomationHost(ProcessPolicy.AllowAll);
        using var form = new System.Windows.Forms.Form();
        var handle = host.Sessions.RegisterNativeWindow(form.Handle, Environment.ProcessId);
        var desktop = new FakeDesktop { ForegroundWindow = FakeDesktop.OtherHwnd };
        var inputs = new InputTally();

        using (desktop.Install())
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                RunExecutor.FocusOwnedWindow(host, handle, journal, inputs)
            );
            Assert.Contains("grant", error.Message);
        }

        Assert.Equal(0, desktop.FocusRequests);
        Assert.Equal(0, inputs.Sent);
        Assert.False(File.Exists(journal));
    }

    [Fact]
    public async Task TheUsersOwnInputSinceTheRunBeganStopsTheFocus()
    {
        using var host = new AutomationHost(ProcessPolicy.AllowAll);
        using var form = new System.Windows.Forms.Form();
        var handle = host.Sessions.RegisterNativeWindow(form.Handle, Environment.ProcessId);
        var desktop = new FakeDesktop { ForegroundWindow = FakeDesktop.OtherHwnd };
        var inputs = new InputTally();
        using var interaction = Approved();

        using (desktop.Install())
        using (interaction.Guard())
        {
            Interlocked.Increment(ref userInput);
            var error = await Assert.ThrowsAnyAsync<Exception>(() =>
                RunExecutor.FocusOwnedWindow(host, handle, journal, inputs)
            );
            Assert.Contains("input from the user", error.ToString());
        }

        Assert.Equal(0, desktop.FocusRequests);
        Assert.Equal(0, inputs.Sent);
    }

    [Fact]
    public async Task FocusingAWindowAlreadyInFrontSendsNothing()
    {
        using var host = new AutomationHost(ProcessPolicy.AllowAll);
        using var form = new System.Windows.Forms.Form();
        var handle = host.Sessions.RegisterNativeWindow(form.Handle, Environment.ProcessId);
        var desktop = new FakeDesktop { ForegroundWindow = form.Handle };
        var inputs = new InputTally();
        using var interaction = Approved();

        using (desktop.Install())
        using (interaction.Guard())
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
        using var interaction = Approved();

        using (desktop.Install())
        using (interaction.Guard())
        {
            var error = await Assert.ThrowsAsync<ProviderBlockedException>(() =>
                RunExecutor.FocusOwnedWindow(host, handle, journal, inputs)
            );
            Assert.Same(pending, error.Pending);
        }

        Assert.Equal(0, desktop.FocusRequests);
        Assert.Equal(0, inputs.Sent);
    }

    [Fact]
    public void TheRunLaunchesTe3OnlyThroughCoresGuardedStartUnderItsGrant()
    {
        var manifest = new RunManifest();
        var desktop = new FakeDesktop();
        var start = new System.Diagnostics.ProcessStartInfo("TabularEditor3.exe");

        using (desktop.Install())
        {
            Assert.Throws<InvalidOperationException>(() => RunExecutor.LaunchTe3(manifest, () => { }, start));
            Assert.Equal(0, desktop.StartRequests);

            using var interaction = Approved();
            using (interaction.Guard())
            {
                // The fake starts nothing, so the launch then fails as "did not start" after one guarded start.
                Assert.Throws<InvalidOperationException>(() => RunExecutor.LaunchTe3(manifest, () => { }, start));
            }
        }

        Assert.Equal(1, desktop.StartRequests);
    }

    [Fact]
    public void AManualActivationCountsTheUsersInputFromTheClickOnward()
    {
        // Windows refused the focus and the run asked the user to click TE3: that click is the go-ahead, so the grant
        // counts input from it; any input after it still stops the run.
        using var interaction = Approved();
        Interlocked.Increment(ref userInput);

        interaction.Rebaseline();
        interaction.Require();

        Interlocked.Increment(ref userInput);
        Assert.Throws<InvalidOperationException>(interaction.Require);
        Assert.Throws<InvalidOperationException>(interaction.Rebaseline);
    }
}
