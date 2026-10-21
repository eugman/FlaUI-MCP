using FlaUI.Mcp;

/// <summary>What a failed run captures to explain itself, and why a capture is missing.</summary>
public static partial class RunExecutor
{
    /// <summary>
    /// Captures the failed run's window and element tree. Both are reads (a background PrintWindow capture with no
    /// pointer move, and a find), so they go straight to Core without the run's grant or cancellation: after the
    /// user's input revoked the grant is when they matter most. Each capture that fails or cannot be taken adds a line
    /// to <see cref="RunManifest.DiagnosticNotes"/>.
    /// </summary>
    /// <param name="read">Runs one Core read tool.</param>
    /// <param name="handle">The run's TE3 window, or null when none was registered.</param>
    internal static async Task CaptureFailure(
        Func<string, object, Task<McpToolResult>> read,
        string? handle,
        string output,
        RunManifest manifest
    )
    {
        if (handle == null)
        {
            manifest.DiagnosticNotes.Add(
                "failure.png and windows_find not captured: no TE3 window was registered before the failure."
            );
            return;
        }

        await Noted(
            manifest,
            "failure.png",
            () =>
                read(
                    "windows_screenshot",
                    new
                    {
                        handle,
                        savePath = Path.Combine(output, "failure.png"),
                        includeImage = false,
                        background = true,
                    }
                )
        );
        await Noted(manifest, "windows_find", () => read("windows_find", new { handle, maxResults = 200 }));
    }

    /// <summary>Writes the z-ordered window list, or notes why it could not.</summary>
    internal static void WriteWindowListNoted(string path, RunManifest manifest)
    {
        try
        {
            WriteWindowList(path, manifest.ProcessId);
        }
        catch (Exception error)
        {
            manifest.DiagnosticNotes.Add($"{Path.GetFileName(path)} not written: {error.Message}");
        }
    }

    private static async Task Noted(RunManifest manifest, string what, Func<Task<McpToolResult>> read)
    {
        try
        {
            var result = await read();
            if (result.IsError == true)
            {
                manifest.DiagnosticNotes.Add(
                    $"{what} not captured: {string.Join(" ", result.Content.Select(c => c.Text))}"
                );
            }
        }
        catch (Exception error)
        {
            manifest.DiagnosticNotes.Add($"{what} not captured: {error.Message}");
        }
    }
}
