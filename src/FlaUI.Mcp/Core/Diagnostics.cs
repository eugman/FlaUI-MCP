using System.Text;

namespace FlaUI.Mcp.Core;

/// <summary>
/// A failure capture: whether the image was written, where, and why not when it was not, plus the process's
/// top-level windows and the target's UI Automation tree (or why that could not be read).
/// </summary>
public sealed record DiagnosticCapture(bool Saved, string? Path, string? SkipReason)
{
    public IReadOnlyList<Win32WindowInfo> Windows { get; init; } = [];

    public string? Tree { get; init; }

    public string? TreeSkipReason { get; init; }
}

/// <summary>
/// Evidence for a failure, taken without the grant: it must work right after a revoke, a cancel or while
/// another call holds the input lease. It renders the window by handle (PrintWindow), lists the process's
/// windows and reads the target's UI Automation tree, each on a worker bounded by a deadline so a hung window
/// cannot hold it up. It takes no lease, makes no permission check, ignores the operation's cancellation, and
/// never focuses or moves the pointer.
/// </summary>
public static class Diagnostics
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Render <paramref name="hwnd"/> to a PNG at <paramref name="path"/>, and write the windows and tree to
    /// <c>path + ".txt"</c>; never throws.
    /// </summary>
    public static DiagnosticCapture Capture(nint hwnd, string path, TimeSpan? timeout = null)
    {
        var deadline = timeout ?? DefaultTimeout;
        var desktop = InputDesktop.Current;
        var (image, skip) = Render(desktop, hwnd, deadline);
        var windows = Bounded(() => desktop.TopLevelWindows(desktop.ProcessOf(hwnd)), deadline, "answer").Value ?? [];
        var (tree, treeError) = Bounded(() => desktop.ElementTree(hwnd), deadline, "answer");
        var capture = new DiagnosticCapture(false, null, skip)
        {
            Windows = windows,
            Tree = tree,
            TreeSkipReason = treeError,
        };
        if (image != null)
        {
            capture = Write(path, image, capture);
        }

        WriteNotes(path + ".txt", capture);
        return capture;
    }

    private static (byte[]? Image, string? Skip) Render(InputDesktop desktop, nint hwnd, TimeSpan deadline)
    {
        var (rendered, error) = Bounded<(byte[]? Image, string? Reason)>(
            () => desktop.CaptureWindow(hwnd, out var image, out var reason) ? (image, null) : (null, reason),
            deadline,
            "render"
        );
        if (error != null)
        {
            return (null, $"The window {error}");
        }

        return rendered.Image != null
            ? (rendered.Image, null)
            : (null, $"The window could not be rendered: {rendered.Reason ?? "no reason given"}");
    }

    private static DiagnosticCapture Write(string path, byte[] image, DiagnosticCapture capture)
    {
        try
        {
            File.WriteAllBytes(path, image);
            return capture with { Saved = true, Path = path, SkipReason = null };
        }
        catch (Exception error)
            when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return capture with { SkipReason = $"The capture could not be written: {error.Message}" };
        }
    }

    private static void WriteNotes(string path, DiagnosticCapture capture)
    {
        var notes = new StringBuilder();
        notes.AppendLine(capture.Saved ? $"Image: {capture.Path}" : $"No image: {capture.SkipReason}");
        notes.AppendLine("Windows:");
        foreach (var window in capture.Windows)
        {
            notes.AppendLine(
                $"  {window.Hwnd} '{window.Title}' enabled={window.IsEnabled} owner={window.Owner} class={window.ClassName}"
            );
        }

        notes.AppendLine(capture.Tree != null ? $"Tree:\n{capture.Tree}" : $"No tree: {capture.TreeSkipReason}");
        try
        {
            File.WriteAllText(path, notes.ToString());
        }
        catch (Exception error)
            when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // The image (or its skip reason) is the evidence; the notes beside it are best effort.
        }
    }

    /// <summary>Run a read on a worker; the value, or why there is none ("did not ... within" or the failure).</summary>
    private static (T? Value, string? Error) Bounded<T>(Func<T> read, TimeSpan deadline, string verb)
    {
        var task = Task.Run(read, CancellationToken.None);
        try
        {
            if (!task.Wait(deadline, CancellationToken.None))
            {
                _ = task.ContinueWith(t => _ = t.Exception, TaskScheduler.Default);
                return (default, $"did not {verb} within {(int)deadline.TotalMilliseconds} ms; it may be hung.");
            }

            return (task.Result, null);
        }
        catch (AggregateException error)
        {
            return (default, $"could not be read: {(error.InnerException ?? error).Message}");
        }
    }
}
