/// <summary>A popup opened for a capture, closed again however the capture ends.</summary>
internal static class PopupScope
{
    /// <summary>
    /// Runs <paramref name="use"/> on a popup <paramref name="find"/> opened, and always <paramref name="close"/>s
    /// it: with its handle as soon as it is known (so a check that fails after that still confirms it closed), or 0
    /// when it was never found, and with the failure, if any.
    /// </summary>
    public static async Task Run<T>(
        Func<Task<(T Popup, nint Hwnd)>> find,
        Func<T, Task> use,
        Func<nint, Exception?, Task> close
    )
    {
        Exception? failure = null;
        nint hwnd = 0;
        try
        {
            var (popup, found) = await find();
            hwnd = found;
            await use(popup);
        }
        catch (Exception error)
        {
            failure = error;
            throw;
        }
        finally
        {
            await close(hwnd, failure);
        }
    }
}
