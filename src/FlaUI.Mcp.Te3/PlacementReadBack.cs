using System.Drawing;

/// <summary>
/// A windows_place_window the page sends: Core fails a placement that did not land exactly (verification "mismatch"),
/// but TE3 dialogs keep their OS minimum sizes on purpose. Only that mismatch, and only growth at the requested
/// top-left, is read back and reported with its deltas; any other failure stays a failure.
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
        catch (CoreCallException error) when (IsMismatch(error))
        {
            var moved =
                readBack()
                ?? throw new InvalidOperationException(
                    "The window was placed, not exactly, and its bounds could not be read back.",
                    error
                );
            if (!GrewToMinimum(requested, moved))
            {
                throw;
            }

            report?.Invoke(
                $"Placement not exact: requested {requested}, read back {moved} ({Deltas(requested, moved)})"
            );
            return moved;
        }

        return readBack() ?? throw new InvalidOperationException("The placed window's bounds could not be read back.");
    }

    // Core's windows_place_window when the move went out in full but the window did not land exactly.
    private static bool IsMismatch(CoreCallException error) =>
        error.Tool == "windows_place_window" && error.Outcome is { Dispatch: "completed", Verification: "mismatch" };

    // An OS minimum size: the same top-left, and no smaller than asked in either direction.
    internal static bool GrewToMinimum(Rectangle requested, Rectangle observed) =>
        observed.Location == requested.Location
        && observed.Width >= requested.Width
        && observed.Height >= requested.Height;

    /// <summary>How far the window landed from the request, e.g. "dx +0, dy +0, dw +14, dh +7".</summary>
    public static string Deltas(Rectangle requested, Rectangle observed) =>
        $"dx {observed.X - requested.X:+0;-0}, dy {observed.Y - requested.Y:+0;-0}, "
        + $"dw {observed.Width - requested.Width:+0;-0}, dh {observed.Height - requested.Height:+0;-0}";
}
