namespace FlaUI.Mcp.Core;

/// <summary>A failure capture: whether it was written, where, and why not when it was not.</summary>
public sealed record DiagnosticCapture(bool Saved, string? Path, string? SkipReason);

/// <summary>
/// Evidence for a failure, taken without the grant: it must work right after a revoke, a cancel or while
/// another call holds the input lease. It renders the window by handle (PrintWindow), so it takes no lease,
/// makes no permission check, ignores the operation's cancellation, and never focuses or moves the pointer.
/// </summary>
public static class Diagnostics
{
    /// <summary>Render <paramref name="hwnd"/> to a PNG at <paramref name="path"/>; never throws.</summary>
    public static DiagnosticCapture Capture(nint hwnd, string path)
    {
        byte[] image;
        try
        {
            if (!InputDesktop.Current.CaptureWindow(hwnd, out image, out var reason))
            {
                return new(false, null, $"The window could not be rendered: {reason ?? "no reason given"}");
            }
        }
        catch (Exception error)
        {
            return new(false, null, $"The window could not be rendered: {error.Message}");
        }

        try
        {
            File.WriteAllBytes(path, image);
            return new(true, path, null);
        }
        catch (Exception error)
            when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new(false, null, $"The capture could not be written: {error.Message}");
        }
    }
}
