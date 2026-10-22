using System.Drawing;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FlaUI.Mcp.Tools;

/// <summary>windows_mouse's reply text: where the pointer landed, and what the tool changed before the input.</summary>
internal static class MouseReplies
{
    // What the tool itself changed before the input (a scroll into view) is reported only when it did.
    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// The landing point in screen pixels and relative to the window's capture origin; for a drag, what UI
    /// Automation reported under the pointer after the release (<paramref name="droppedOn"/>).
    /// </summary>
    public static string Landed(string action, Point landed, Rectangle origin, string? changed, string? droppedOn) =>
        JsonSerializer.Serialize(
            new
            {
                action,
                screen = new { x = landed.X, y = landed.Y },
                window = new { x = landed.X - origin.X, y = landed.Y - origin.Y },
                changed,
                droppedOn,
            },
            Options
        );
}
