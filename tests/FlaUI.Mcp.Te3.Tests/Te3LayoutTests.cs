using Xunit;

/// <summary>Pane title matching for te3_layout; no desktop access.</summary>
public sealed class Te3LayoutTests
{
    [Fact]
    public void LayoutCloseNeverTargetsDocumentTabs()
    {
        Assert.True(Te3Layout.CanCloseKind("pane"));
        Assert.False(Te3Layout.CanCloseKind("document"));
    }

    [Fact]
    public void AssistantReviewPresetRequiresPresentationSize()
    {
        var sizes = FlaUI.Mcp.Te3.Te3PanePlan.Resolve(FlaUI.Mcp.Te3.Te3PanePlan.AssistantModelReview, 1920, 1080);
        Assert.Equal(1000, sizes.Single(s => s.Pane == "AI Assistant").Width);
        Assert.Equal(700, sizes.Single(s => s.Pane == "TOM Explorer").Width);
        Assert.Equal(300, sizes.Single(s => s.Pane == "Properties").Height);
        Assert.Throws<ArgumentException>(() =>
            FlaUI.Mcp.Te3.Te3PanePlan.Resolve(FlaUI.Mcp.Te3.Te3PanePlan.AssistantModelReview, 1600, 900)
        );
    }

    [Theory]
    [InlineData("TOM Explorer", "TOM Explorer")]
    [InlineData("TOM Explorer (Changed)", "TOM Explorer")]
    [InlineData("TOM Explorer (Changed)", "TOM Explorer (Changed)")]
    [InlineData("Properties", "Properties (Changed)")]
    public void ShowChangesSuffixIsIgnored(string pane, string requested) =>
        Assert.True(Te3Layout.TitleMatches(pane, requested));

    [Theory]
    [InlineData("TOM Explorer", "Properties")]
    [InlineData("TOM Explorer", "tom explorer")]
    [InlineData("Best Practice Analyzer", "Best Practice")]
    public void OtherTitlesDoNotMatch(string pane, string requested) =>
        Assert.False(Te3Layout.TitleMatches(pane, requested));
}
