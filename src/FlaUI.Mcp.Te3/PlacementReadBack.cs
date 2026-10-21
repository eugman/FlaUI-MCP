using System.Drawing;

/// <summary>
/// A windows_place_window the page sends: Core fails a placement that did not land exactly, but TE3 dialogs keep their
/// OS minimum sizes on purpose. A failure after the window moved is read back and reported with its deltas instead.
/// </summary>
internal static class PlacementReadBack
{
    /// <summary>Places, then returns the window's bounds as read back.</summary>
    /// <param name="place">The placement call.</param>
    /// <param name="readBack">The window's bounds now, or null when they cannot be read.</param>
    public static async Task<Rectangle> Place(
        Func<Task> place,
        Func<Rectangle?> readBack,
        Rectangle requested,
        Action<string>? report
    )
    {
        try
        {
            await place();
        }
        catch (CoreCallException error) when (error.Outcome.InputsSent > 0)
        {
            var moved =
                readBack()
                ?? throw new InvalidOperationException(
                    "The window was placed, not exactly, and its bounds could not be read back.",
                    error
                );
            report?.Invoke(
                $"Placement not exact: requested {requested}, read back {moved} ({Deltas(requested, moved)})"
            );
            return moved;
        }

        return readBack() ?? throw new InvalidOperationException("The placed window's bounds could not be read back.");
    }

    /// <summary>How far the window landed from the request, e.g. "dx +0, dy +0, dw +14, dh +7".</summary>
    public static string Deltas(Rectangle requested, Rectangle observed) =>
        $"dx {observed.X - requested.X:+0;-0}, dy {observed.Y - requested.Y:+0;-0}, "
        + $"dw {observed.Width - requested.Width:+0;-0}, dh {observed.Height - requested.Height:+0;-0}";
}
