using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>
/// The end of a run on a scripted process and a temporary settings folder: nothing here starts, finds or closes a
/// real process. The run's grant is installed as it is in RunExecutor.Execute, through the cleanup.
/// </summary>
public sealed class RunCleanupTests : IDisposable
{
    private const string Original = """{"Skin":"user"}""";

    private readonly string root = Path.Combine(Path.GetTempPath(), "fla_run_cleanup_" + Guid.NewGuid().ToString("N"));
    private readonly string settings;
    private readonly string manifestPath;
    private readonly RunManifest manifest = new() { Passed = true };
    private readonly Te3Interaction interaction;
    private long input;

    public RunCleanupTests()
    {
        settings = Path.Combine(root, "settings");
        Directory.CreateDirectory(settings);
        File.WriteAllText(Preferences, Original);
        manifestPath = Path.Combine(root, "manifest.json");
        interaction = new Te3Interaction(() => input);
        interaction.Begin("FlaUI.Mcp.Te3 run test", userApproved: true);
    }

    public void Dispose()
    {
        interaction.Dispose();
        Directory.Delete(root, true);
    }

    private string Preferences => Path.Combine(settings, "UiPreferences.json");

    private string Backup => Path.Combine(root, "backup");

    private void Save() => AtomicJournal.Write(manifestPath, manifest, RunConfig.Json);

    // Stages the settings as a run does, then runs its cleanup under the run's guard.
    private async Task Finish(FakeOwnedProcess te3, bool revoke)
    {
        using var guard = interaction.Guard();
        var before = SettingsChanges.Snapshot(settings);
        var lease = RunExecutor.AcquireSettings(
            manifest,
            Save,
            () => new SettingsLease(settings, Backup, manifestPath)
        );
        lease.Normalize();
        if (revoke)
        {
            // The user touches the machine mid-recipe: the grant is revoked before the cleanup starts.
            input++;
        }

        await new RunCleanup(manifest, settings, Save)
        {
            Settings = lease,
            SettingsBefore = before,
            CloseOwned = () => RunSafety.CloseOwned(te3, TimeSpan.Zero),
            RequireNoTe3 = () => RunSafety.RequireExclusiveTe3(running: () => te3.Exited ? [] : [4242]),
            WriteIndex = () => { },
        }.Run();
        if (revoke)
        {
            Assert.Equal(
                Te3Interaction.FocusRevoked,
                Assert.Throws<InputRefusedException>(MutationGuard.CheckPermission).Refusal.Code
            );
        }
    }

    [Fact]
    public async Task ARevokedGrantStillClosesTheRunsTe3AndRestoresThePreferences()
    {
        var te3 = new FakeOwnedProcess(Preferences);

        await Finish(te3, revoke: true);

        Assert.Equal(["verify", "close"], te3.Calls);
        Assert.Empty(manifest.DiagnosticNotes);
        Assert.Equal(Original, File.ReadAllText(Preferences));
        Assert.True(manifest.SettingsRestored);
        Assert.False(manifest.NeedsRecovery);
        Assert.Null(manifest.CleanupError);
        Assert.False(ArtifactFiles.ReadManifest(manifestPath).NeedsRecovery);
        Assert.False(Directory.Exists(Backup));
    }

    [Fact]
    public async Task ATe3ThatIgnoresItsCloseIsKilledAfterAnotherIdentityCheck()
    {
        var te3 = new FakeOwnedProcess(Preferences) { ExitsOnClose = false };

        await Finish(te3, revoke: true);

        Assert.Equal(["verify", "close", "verify", "kill"], te3.Calls);
        Assert.Contains(RunCleanup.KilledNote, ArtifactFiles.ReadManifest(manifestPath).DiagnosticNotes);
        Assert.Equal(Original, File.ReadAllText(Preferences));
        Assert.False(manifest.NeedsRecovery);
    }

    [Fact]
    public async Task AProcessThatIsNotTheRunsOwnIsNeverClosedAndTheRunNeedsRecovery()
    {
        var te3 = new FakeOwnedProcess(Preferences) { Mismatch = true };

        await Finish(te3, revoke: false);

        Assert.Equal(["verify"], te3.Calls);
        Assert.True(manifest.NeedsRecovery);
        Assert.False(manifest.Passed);
        Assert.False(manifest.SettingsRestored);
        Assert.Contains("TE3 shutdown: Process identity mismatch", manifest.CleanupError);
        Assert.Contains(Backup, manifest.CleanupError);
        Assert.True(Directory.Exists(Backup));
        Assert.True(ArtifactFiles.ReadManifest(manifestPath).NeedsRecovery);
    }
}
