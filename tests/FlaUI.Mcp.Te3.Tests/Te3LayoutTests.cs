using Xunit;

/// <summary>Pane title matching for te3_layout; no desktop access.</summary>
public sealed class Te3LayoutTests
{
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
