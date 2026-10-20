using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>What the run journal persists around a TE3 launch; nothing is launched.</summary>
public sealed class RunJournalTests
{
    [Fact]
    public void ALaunchThatStartsNothingFailsAfterSavingTheIntent()
    {
        var manifest = new RunManifest();
        var saves = 0;

        var error = Assert.Throws<InvalidOperationException>(() =>
            RunJournal.Launch(
                manifest,
                () => saves++,
                new System.Diagnostics.ProcessStartInfo("TabularEditor3.exe"),
                _ => null
            )
        );

        Assert.Contains("did not start", error.Message);
        Assert.Equal(1, saves);
        Assert.NotEqual(0, manifest.LaunchRequestedTicks);
        Assert.Equal(0, manifest.ProcessId);
    }
}
