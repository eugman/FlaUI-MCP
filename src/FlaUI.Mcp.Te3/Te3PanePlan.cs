namespace FlaUI.Mcp.Te3;

public sealed record PaneSize(string Pane, int? Width = null, int? Height = null);

/// <summary>Layout targets for the 1920x1080 TE3 demonstration surface.</summary>
public static class Te3PanePlan
{
    public const string AssistantModelReview = "assistant-model-review";

    public static PaneSize[] Resolve(string preset, int windowWidth, int windowHeight)
    {
        if (preset != AssistantModelReview)
        {
            throw new ArgumentException("Unknown TE3 layout preset.");
        }

        if (windowWidth < 1800 || windowHeight < 900)
        {
            throw new ArgumentException("Assistant model review needs a window at least 1800x900 physical pixels.");
        }

        // Reserve 40 pixels for splitter/chrome: at 1800 pixels the editor retains at least 600.
        return [new("AI Assistant", Width: 680), new("TOM Explorer", Width: 480), new("Properties", Height: 320)];
    }

    public static bool IsNamedPane(string pane) => pane is "AI Assistant" or "TOM Explorer" or "Properties";
}
