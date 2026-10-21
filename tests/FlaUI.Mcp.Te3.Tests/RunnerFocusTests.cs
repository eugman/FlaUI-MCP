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

    /// <summary>The user's keys and clicks, counted apart as the real observer counts them.</summary>
    private sealed class CountingObserver : IInputObserver
    {
        public long Keys { get; set; }
        public long ButtonDowns { get; set; }
        public long Moves { get; set; }

        public long Sequence => Keys + ButtonDowns + Moves;

        public System.Drawing.Point? LastButtonDown { get; set; } = new System.Drawing.Point(500, 400);

        public InputCounts Counts => new(Keys, ButtonDowns, LastButtonDown);

        public void Dispose() { }
    }

    // The run's TE3 window, placed at (0,0,1920,1080).
    private static readonly System.Drawing.Rectangle Te3Window = new(0, 0, 1920, 1080);

    private static (Te3Interaction Interaction, CountingObserver User) Observed()
    {
        var user = new CountingObserver();
        var interaction = new Te3Interaction(() => user);
        interaction.Begin("run model-open", userApproved: true);
        return (interaction, user);
    }

    [Fact]
    public void AClickThatBroughtTe3ForwardCountsTheUsersInputFromThere()
    {
        // Windows refused the focus and the run asked the user to click TE3: that click is the go-ahead, so the grant
        // counts input from it; any input after it still stops the run.
        var (interaction, user) = Observed();
        using var _ = interaction;
        var prompt = interaction.Mark();
        user.Moves++;
        user.ButtonDowns++;

        interaction.Rebaseline(prompt, Te3Window);
        interaction.Require();

        user.Moves++;
        Assert.Equal(
            Te3Interaction.FocusRevoked,
            Assert.Throws<InputRefusedException>(interaction.Require).Refusal.Code
        );
    }

    [Fact]
    public void TypingElsewhereWhileTe3CameForwardIsRefusedNotAbsorbed()
    {
        // The user answered mail in Outlook during the 120 s wait, closed it, and TE3 came to the front; a click had
        // happened too. Every key went somewhere the run cannot see, so the run must not carry on.
        var (interaction, user) = Observed();
        using var _ = interaction;
        var prompt = interaction.Mark();
        user.Keys += 40;
        user.ButtonDowns++;

        var error = Assert.Throws<InputRefusedException>(() => interaction.Rebaseline(prompt, Te3Window));

        Assert.Equal(Te3Interaction.ActivationDenied, error.Refusal.Code);
        Assert.Contains("keyboard", error.Message);
        Assert.Throws<InputRefusedException>(interaction.Require);
    }

    [Fact]
    public void AClickOutsideTe3IsNotTheClickTheRunAskedFor()
    {
        // The user clicked their own window on a second monitor; TE3 then came forward when that window closed.
        var (interaction, user) = Observed();
        using var _ = interaction;
        var prompt = interaction.Mark();
        user.ButtonDowns++;
        user.LastButtonDown = new System.Drawing.Point(2500, 300);

        var error = Assert.Throws<InputRefusedException>(() => interaction.Rebaseline(prompt, Te3Window));

        Assert.Equal(Te3Interaction.ActivationDenied, error.Refusal.Code);
        Assert.Contains("not inside the TE3 window", error.Message);
    }

    [Fact]
    public void Te3ComingForwardWithoutAClickIsRefused()
    {
        // A late foreground grant, or the user's own window closing, brought TE3 forward: nobody clicked it.
        var (interaction, user) = Observed();
        using var _ = interaction;
        var prompt = interaction.Mark();
        user.Moves += 5;

        var error = Assert.Throws<InputRefusedException>(() => interaction.Rebaseline(prompt, Te3Window));

        Assert.Equal(Te3Interaction.ActivationDenied, error.Refusal.Code);
        Assert.Contains("without a click", error.Message);
    }

    [Fact]
    public void AGrantWithoutAnObserverThatCountsClicksCannotBeContinued()
    {
        using var interaction = Approved();

        Assert.Throws<InputRefusedException>(() => interaction.Rebaseline(interaction.Mark(), Te3Window));
    }

    [Fact]
    public void ARevokedGrantIsNotContinuedByAClick()
    {
        var (interaction, user) = Observed();
        using var _ = interaction;
        var prompt = interaction.Mark();
        user.Keys++;
        Assert.Throws<InputRefusedException>(interaction.Require);
        user.ButtonDowns++;

        Assert.Throws<InputRefusedException>(() => interaction.Rebaseline(prompt, Te3Window));
    }

    [Theory]
    [InlineData(0x0201, true)] // WM_LBUTTONDOWN
    [InlineData(0x0204, true)] // WM_RBUTTONDOWN
    [InlineData(0x0207, true)] // WM_MBUTTONDOWN
    [InlineData(0x020B, true)] // WM_XBUTTONDOWN
    [InlineData(0x0200, false)] // WM_MOUSEMOVE
    [InlineData(0x0202, false)] // WM_LBUTTONUP
    [InlineData(0x020A, false)] // WM_MOUSEWHEEL
    public void OnlyAButtonGoingDownCountsAsAClick(int message, bool down) =>
        Assert.Equal(down, PhysicalInputObserver.IsButtonDown(message));
}
