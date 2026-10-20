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
        Assert.Equal(680, sizes.Single(s => s.Pane == "AI Assistant").Width);
        Assert.Equal(480, sizes.Single(s => s.Pane == "TOM Explorer").Width);
        Assert.Equal(320, sizes.Single(s => s.Pane == "Properties").Height);
        Assert.Throws<ArgumentException>(() =>
            FlaUI.Mcp.Te3.Te3PanePlan.Resolve(FlaUI.Mcp.Te3.Te3PanePlan.AssistantModelReview, 1600, 900)
        );
    }

    [Fact]
    public void PresetGuardAlwaysLeavesTheMinimumEditorWidth()
    {
        // Calls the real guard: the narrowest accepted window, minus every side pane and the chrome reserve,
        // is exactly the minimum editor width, and one pixel less is refused.
        var preset = FlaUI.Mcp.Te3.Te3PanePlan.AssistantModelReview;
        var probe = FlaUI.Mcp.Te3.Te3PanePlan.Resolve(preset, 1920, 1080);
        var narrowest = FlaUI.Mcp.Te3.Te3PanePlan.MinimumWindowWidth(probe);
        var sizes = FlaUI.Mcp.Te3.Te3PanePlan.Resolve(preset, narrowest, 900);
        var editor = narrowest - sizes.Sum(s => s.Width ?? 0) - FlaUI.Mcp.Te3.Te3PanePlan.ChromeReserve;
        Assert.Equal(FlaUI.Mcp.Te3.Te3PanePlan.MinimumEditorWidth, editor);
        Assert.Throws<ArgumentException>(() => FlaUI.Mcp.Te3.Te3PanePlan.Resolve(preset, narrowest - 1, 900));
        Assert.Throws<ArgumentException>(() => FlaUI.Mcp.Te3.Te3PanePlan.Resolve(preset, narrowest, 899));
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

    [Fact]
    public async Task PlanPresetDoesNotRequireTe3Process()
    {
        var tool = new Te3Layout(null!);
        var args = System.Text.Json.JsonSerializer.SerializeToElement(
            new
            {
                action = "plan-preset",
                preset = FlaUI.Mcp.Te3.Te3PanePlan.AssistantModelReview,
                windowWidth = 1800,
                windowHeight = 900,
            }
        );
        var result = await tool.ExecuteAsync(args);
        Assert.NotEqual(true, result.IsError);
    }
}
