using Xunit;

/// <summary>
/// Defects found reviewing this branch before it was pushed. Each test pins the specific wrong
/// behaviour rather than the fix, so a regression reads as the original bug.
/// </summary>
public sealed class ReviewFixTests
{
    [Fact]
    public void OnlyLeasedFilesAreARecoveryCondition()
    {
        // TE3 rewrites MRU, session and cache files on a normal launch. Treating those as a
        // recovery condition marked every run unrecoverable, and Promote refuses every capture
        // from such a run — so one benign file blocked the whole pipeline with no way to clear it.
        var changes = new[] { "changed: RecentConnections.json", "added: AI/chat-state.json" };
        Assert.Empty(SettingsChanges.Unrestored(changes));
    }

    [Fact]
    public void AChangedLeasedFileStillNeedsRecovery()
    {
        // This one has a backup and a human action that clears it.
        var changes = new[] { "changed: RecentConnections.json", "changed: Preferences.json" };
        Assert.Equal(["changed: Preferences.json"], SettingsChanges.Unrestored(changes));
    }

    [Theory]
    [InlineData(0, 0, 100, 40)]
    [InlineData(-1920, -200, 800, 600)]
    [InlineData(0, 0, 10, 1)]
    public void TheParkPointIsInsideTheWindow(int x, int y, int width, int height)
    {
        var bounds = new System.Drawing.Rectangle(x, y, width, height);
        Assert.True(bounds.Contains(Te3Page.ParkPoint(bounds)));
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(0, 0, 10, 0)]
    public void ADegenerateWindowHasNoPointToPark(int x, int y, int width, int height)
    {
        // Clamping into an empty rectangle returns a pixel outside the window, which then fails
        // the obscured check as though the title bar were covered.
        Assert.Throws<InvalidOperationException>(() =>
            Te3Page.ParkPoint(new System.Drawing.Rectangle(x, y, width, height))
        );
    }

    [Fact]
    public void PanelsWithChildrenDoNotFailTheStatusBarRead()
    {
        // BoundedSearch flags Truncated when a node at the depth limit has children, so with
        // maxDepth 1 it meant "there is content inside a panel" — normal for a WinForms status
        // strip. Requiring Complete made te3_model_state throw on a bar it could read perfectly.
        Assert.Null(Te3Page.ModelStateReadFailure(count: 7, limit: 40, unreadable: 0));
    }

    [Fact]
    public void PanelsLostAtTheResultCapFailTheRead()
    {
        // This is what would actually corrupt a positional mapping.
        var failure = Te3Page.ModelStateReadFailure(count: 40, limit: 40, unreadable: 0);
        Assert.NotNull(failure);
        Assert.Contains("dropped at the cap", failure);
    }

    [Fact]
    public void AnUnreadablePanelFailsTheRead()
    {
        var failure = Te3Page.ModelStateReadFailure(count: 7, limit: 40, unreadable: 1);
        Assert.NotNull(failure);
        Assert.Contains("could not be read", failure);
    }
}
