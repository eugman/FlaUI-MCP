namespace FlaUI.Mcp.Te3;

public sealed record PaneSize(string Pane, int? Width = null, int? Height = null);

/// <summary>Layout targets for the 1920x1080 TE3 demonstration surface.</summary>
public static class Te3PanePlan
{
    public const string AssistantModelReview = "assistant-model-review";

    /// <summary>Splitters and window chrome between the side panes and the document area.</summary>
    public const int ChromeReserve = 40;

    /// <summary>Document editor width the preset always leaves for DAX and model text.</summary>
    public const int MinimumEditorWidth = 600;

    public const int MinimumWindowHeight = 900;

    private static readonly PaneSize[] AssistantModelReviewSizes =
    [
        new("AI Assistant", Width: 680),
        new("TOM Explorer", Width: 480),
        new("Properties", Height: 320),
    ];

    /// <summary>Narrowest window that fits every side pane and still leaves the minimum editor width.</summary>
    public static int MinimumWindowWidth(IEnumerable<PaneSize> sizes) =>
        sizes.Sum(size => size.Width ?? 0) + ChromeReserve + MinimumEditorWidth;

    public static PaneSize[] Resolve(string preset, int windowWidth, int windowHeight)
    {
        if (preset != AssistantModelReview)
        {
            throw new ArgumentException("Unknown TE3 layout preset.");
        }

        var minimumWidth = MinimumWindowWidth(AssistantModelReviewSizes);
        if (windowWidth < minimumWidth || windowHeight < MinimumWindowHeight)
        {
            throw new ArgumentException(
                $"Assistant model review needs a window at least {minimumWidth}x{MinimumWindowHeight} physical pixels."
            );
        }

        return [.. AssistantModelReviewSizes];
    }

    public static bool IsNamedPane(string pane) => pane is "AI Assistant" or "TOM Explorer" or "Properties";
}
