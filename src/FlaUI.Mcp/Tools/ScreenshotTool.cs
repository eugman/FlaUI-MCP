using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Mcp.Core;

namespace FlaUI.Mcp.Tools;

/// <summary>
/// Take a screenshot
/// </summary>
public partial class ScreenshotTool : ToolBase
{
    private readonly SessionManager _sessionManager;
    private readonly ElementRegistry _elementRegistry;
    private readonly PendingInvokeTracker _invokeTracker;
    private readonly ProcessPolicy _processPolicy;

    public ScreenshotTool(
        SessionManager sessionManager,
        ElementRegistry elementRegistry,
        PendingInvokeTracker? invokeTracker = null,
        ProcessPolicy? processPolicy = null
    )
    {
        _sessionManager = sessionManager;
        _elementRegistry = elementRegistry;
        _invokeTracker = invokeTracker ?? new PendingInvokeTracker();
        _processPolicy = processPolicy ?? ProcessPolicy.AllowAll;
    }

    public override string Name => "windows_screenshot";

    protected override bool Observation => true;

    protected override bool Recovery => true;

    protected override IReadOnlyList<string> MutatingWhenArgument => ["parkPointer"];

    // Not read-only: parkPointer moves the pointer off the window before capturing, and that is counted.
    public override McpToolAnnotations Annotations => McpToolAnnotations.Input(destructive: false, idempotent: true);

    public override string Description =>
        "Capture a window, element or the screen as PNG. Normal capture uses UIA bounds and may omit the title bar; strictNative captures the whole native window. With savePath and includeImage=false only the path is returned; view the saved image before claiming a visual result.";

    public override object InputSchema =>
        new
        {
            type = "object",
            additionalProperties = false,
            properties = new
            {
                frame = new
                {
                    type = "object",
                    description = "Optional exact source-size assertion and pixel crop; no rescaling.",
                    properties = new
                    {
                        sourceWidth = new
                        {
                            type = "integer",
                            minimum = 1,
                            maximum = 8192,
                        },
                        sourceHeight = new
                        {
                            type = "integer",
                            minimum = 1,
                            maximum = 8192,
                        },
                        x = new { type = "integer", minimum = 0 },
                        y = new { type = "integer", minimum = 0 },
                        width = new { type = "integer", minimum = 1 },
                        height = new { type = "integer", minimum = 1 },
                    },
                    required = new[] { "x", "y", "width", "height" },
                },
                handle = new
                {
                    type = "string",
                    description = "Window handle. If omitted, captures the foreground window.",
                },
                @ref = new { type = "string", description = "Element ref to capture." },
                fullScreen = new
                {
                    type = "boolean",
                    description = "Capture the entire screen (default: false). Disabled while an app allowlist is active.",
                },
                background = new
                {
                    type = "boolean",
                    description = "Native whole-window capture. A handle falls back to screen pixels; a Window ref does not. Not with fullScreen.",
                },
                strictNative = new
                {
                    type = "boolean",
                    description = "Native whole-window capture with no screen-pixel fallback. Needs a handle or Window ref.",
                },
                savePath = new
                {
                    type = "string",
                    description = "Absolute local .png file path to save the screenshot. UNC and device paths are rejected.",
                },
                overwrite = new
                {
                    type = "boolean",
                    description = "Allow savePath to replace an existing file (default: false)",
                },
                includeImage = new
                {
                    type = "boolean",
                    description = "Return image payload (default true). Set false with savePath for compact artifact-only output.",
                },
                includeMetadata = new
                {
                    type = "boolean",
                    description = "Append capture method, bounds, crop, size and DPI.",
                },
                locate = new
                {
                    type = "array",
                    items = new { type = "string" },
                    description = "Element refs whose rectangles to measure in the returned image, reported as "
                        + "{ref, x, y, width, height, clipped} in image pixels. Use this instead of converting "
                        + "windows_find bounds yourself: those are screen coordinates from an earlier observation, they "
                        + "go stale when the layout moves, and the offset between the two is not constant across DPI "
                        + "states. Implies includeMetadata. Refused when the capture cannot be trusted to map 1:1.",
                },
                parkPointer = new
                {
                    type = "boolean",
                    description = "Move the pointer off the window first, so no tooltip or hover highlight lands in the "
                        + "image (default false). Use it for any capture that will be published.",
                },
                allowOccludedPixels = new
                {
                    type = "boolean",
                    description = "Capture screen pixels even when another application's window covers part of the "
                        + "target (default false). Only for deliberately capturing a partly covered window: the image "
                        + "will contain whatever is on top. strictNative is usually the better answer, since it renders "
                        + "the window's own surface and is unaffected by what is in front of it.",
                },
            },
        };

    /// <summary>
    /// The call in its steps: read and check the request, park the pointer, capture by the path the target allows,
    /// then judge, annotate and write the result.
    /// </summary>
    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments) => Task.FromResult(Execute(arguments));

    private McpToolResult Execute(JsonElement? arguments)
    {
        if (ReadRequest(arguments, out var request) is { } invalid)
        {
            return invalid;
        }

        // Park only once the call is known to be valid, so a refused call never moves the real mouse. A park the
        // guards refuse (a revoked grant, a busy lease) still captures, with a warning: the image is the evidence.
        var shot = new Shot();
        if (request.ParkPointer)
        {
            var parkError = ParkPointer(request.Handle, request.RefId, out var parkWarning, out var parkProof);
            if (parkError != null)
            {
                return ErrorResult(parkError);
            }

            (shot.ParkWarning, shot.ParkProof) = (parkWarning, parkProof);
        }

        try
        {
            OperationContext.Check();
            return CaptureTarget(request, shot);
        }
        catch (Exception ex)
        {
            return ErrorResult($"Failed to capture screenshot: {ex.Message}");
        }
    }

    /// <summary>The warning for a park the guards refused; the capture goes ahead without it.</summary>
    internal static string ParkRefused(Exception error) =>
        $"parkPointer was refused, so the pointer was not moved: {error.Message} "
        + "The capture was taken anyway; a hover state or tooltip may show in it.";

    /// <summary>Whether the pointer ended outside the window, read back after the park; unavailable when refused.</summary>
    internal static Proof ParkProof(System.Drawing.Rectangle window, System.Drawing.Point? landed)
    {
        var expected = $"outside {window.X},{window.Y} {window.Width}x{window.Height}";
        if (landed is not { } point)
        {
            return new(Proof.Unavailable, null, expected, "pointer", Independent: true);
        }

        var verdict = window.Contains(point) ? Proof.Mismatch : Proof.Verified;
        return new(verdict, $"{point.X},{point.Y}", expected, "pointer", Independent: true);
    }

    private string? ParkPointer(string? handle, string? refId, out string? warning, out Proof? proof)
    {
        warning = null;
        proof = null;
        var window = handle ?? (refId == null ? null : _elementRegistry.WindowForRef(refId));
        var hwnd = window == null ? InputDesktop.Current.Foreground() : _sessionManager.GetWindowHwnd(window);
        if (window != null && WindowIsGone(hwnd))
        {
            return ClosedWindowMessage(window);
        }

        if (!_processPolicy.IsProcessAllowed(InputDesktop.Current.ProcessOf(hwnd)))
        {
            return _processPolicy.DescribeDenied("parkPointer target");
        }

        if (InputDesktop.Current.WindowBounds(hwnd) is not { } bounds)
        {
            return "parkPointer: window bounds unavailable; no screenshot was taken.";
        }

        try
        {
            proof = ParkProof(bounds, PointerParking.Park(bounds));
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or AutomationRefusal)
        {
            warning = ParkRefused(ex);
            proof = ParkProof(bounds, null);
        }

        return null;
    }

    // A cached handle can outlive its window (a dialog that closed, an app that exited). Report that,
    // not the allowlist refusal its dead process would otherwise produce.
    internal static bool WindowIsGone(nint hwnd) => hwnd != 0 && InputDesktop.Current.ProcessOf(hwnd) == 0;

    internal static string ClosedWindowMessage(string handle) =>
        $"Window {handle} has closed. Get a current handle from windows_list_windows; no screenshot was taken.";

    internal static McpToolResult BuildScreenshotResult(
        byte[] imageData,
        string? savePath,
        bool overwrite,
        bool includeImage,
        CaptureFrame? frame = null
    )
    {
        OperationContext.Check();
        if (frame != null)
        {
            imageData = frame.Apply(imageData);
        }

        OperationContext.Check();
        if (string.IsNullOrEmpty(savePath))
        {
            return ImageResult(imageData, "image/png");
        }

        if (ScreenshotChecks.SaveAtomically(savePath, imageData, overwrite) is { } saveError)
        {
            return ErrorResult(saveError);
        }

        if (!includeImage)
        {
            return frame == null
                ? TextResult(JsonSerializer.Serialize(new { path = savePath, bytes = imageData.Length }))
                : TextResult(
                    JsonSerializer.Serialize(
                        new
                        {
                            path = savePath,
                            bytes = imageData.Length,
                            frame,
                        }
                    )
                );
        }

        return new McpToolResult
        {
            Content = new List<McpContent>
            {
                new() { Type = "text", Text = $"Screenshot saved to {savePath}" },
                new()
                {
                    Type = "image",
                    Data = Convert.ToBase64String(imageData),
                    MimeType = "image/png",
                },
            },
        };
    }
}
