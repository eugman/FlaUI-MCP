using FlaUI.Mcp.Core;

/// <summary>Whether a control is on screen, where a read that failed is never taken for absence.</summary>
internal static class ScreenPresence
{
    /// <summary>
    /// The answer of <paramref name="visible"/>; false only when the control was not found. Any other failure (an
    /// incomplete search, a COM or provider error) throws, so a caller does not act on an absence nobody saw.
    /// </summary>
    /// <param name="what">The control, for the failure text.</param>
    public static bool Read(Func<bool> visible, string what)
    {
        try
        {
            return visible();
        }
        catch (ElementNotFoundException)
        {
            return false;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"Could not read whether {what} is on screen, so its absence is not known: {error.Message}",
                error
            );
        }
    }
}
