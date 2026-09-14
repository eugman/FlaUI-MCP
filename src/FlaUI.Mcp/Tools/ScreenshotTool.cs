using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using PlaywrightWindows.Mcp.Core;

namespace PlaywrightWindows.Mcp.Tools;

/// <summary>
/// Take a screenshot
/// </summary>
public class ScreenshotTool : ToolBase
{
    private readonly SessionManager _sessionManager;
    private readonly ElementRegistry _elementRegistry;
    private readonly PendingInvokeTracker _invokeTracker;
    private readonly ProcessPolicy _processPolicy;

    public ScreenshotTool(SessionManager sessionManager, ElementRegistry elementRegistry, PendingInvokeTracker? invokeTracker = null, ProcessPolicy? processPolicy = null)
    {
        _sessionManager = sessionManager;
        _elementRegistry = elementRegistry;
        _invokeTracker = invokeTracker ?? new PendingInvokeTracker();
        _processPolicy = processPolicy ?? ProcessPolicy.AllowAll;
    }

    public override string Name => "windows_screenshot";

    public override string Description => 
        "Capture a window, element or the screen as PNG. Normal capture uses UIA bounds and may omit the title bar; strictNative captures the whole native window. With savePath and includeImage=false only the path is returned; view the saved image before claiming a visual result.";

    public override object InputSchema => new
    {
        type = "object",
        properties = new
        {
            frame = new { type = "object", description = "Optional exact source-size assertion and pixel crop; no rescaling.", properties = new
            {
                sourceWidth = new { type = "integer", minimum = 1, maximum = 8192 },
                sourceHeight = new { type = "integer", minimum = 1, maximum = 8192 },
                x = new { type = "integer", minimum = 0 }, y = new { type = "integer", minimum = 0 },
                width = new { type = "integer", minimum = 1 }, height = new { type = "integer", minimum = 1 }
            }, required = new[] { "x", "y", "width", "height" } },
            handle = new
            {
                type = "string",
                description = "Window handle. If omitted, captures the foreground window."
            },
            @ref = new
            {
                type = "string",
                description = "Element ref to capture."
            },
            fullScreen = new
            {
                type = "boolean",
                description = "Capture the entire screen (default: false). Disabled while an app allowlist is active."
            },
            background = new
            {
                type = "boolean",
                description = "Native whole-window capture. A handle falls back to screen pixels; a Window ref does not. Not with fullScreen."
            },
            strictNative = new
            {
                type = "boolean",
                description = "Native whole-window capture with no screen-pixel fallback. Needs a handle or Window ref."
            },
            savePath = new
            {
                type = "string",
                description = "Absolute local .png file path to save the screenshot. UNC and device paths are rejected."
            },
            overwrite = new
            {
                type = "boolean",
                description = "Allow savePath to replace an existing file (default: false)"
            },
            includeImage = new
            {
                type = "boolean",
                description = "Return image payload (default true). Set false with savePath for compact artifact-only output."
            },
            includeMetadata = new { type = "boolean", description = "Append capture method, bounds, crop, size and DPI." }
        }
    };

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var handle = GetStringArgument(arguments, "handle");
        var refId = GetStringArgument(arguments, "ref");
        if (refId != null)
        {
            try { _elementRegistry.ResolveRef(refId, handle); }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            { return Task.FromResult(ErrorResult(ex.Message)); }
        }
        var fullScreen = GetBoolArgument(arguments, "fullScreen", false);
        var background = GetBoolArgument(arguments, "background", false);
        var strictNative = GetBoolArgument(arguments, "strictNative", false);
        background |= strictNative;
        var savePath = GetStringArgument(arguments, "savePath");
        var overwrite = GetBoolArgument(arguments, "overwrite", false);
        var includeImage = GetBoolArgument(arguments, "includeImage", true);
        var frame = arguments is { } a && a.TryGetProperty("frame", out var f) && f.ValueKind != JsonValueKind.Null
            ? f.Deserialize<CaptureFrame>() : null;
        frame?.Validate();
        if (!includeImage && string.IsNullOrWhiteSpace(savePath))
            return Task.FromResult(ErrorResult("includeImage=false requires savePath"));

        if (!TryNormalizeSavePath(savePath, overwrite, out var normalizedSavePath, out var pathError))
        {
            return Task.FromResult(ErrorResult(pathError));
        }

        try
        {
            OperationContext.Check();
            CaptureImage capture;
            var method = "screen-uia-bounds";
            System.Drawing.Rectangle? sourceBounds = null;
            nint targetHwnd = 0;
            McpToolResult Finish(byte[] bytes)
            {
                if (!GetBoolArgument(arguments, "includeMetadata"))
                    return BuildScreenshotResult(bytes, normalizedSavePath, overwrite, includeImage, frame);
                using var stream = new MemoryStream(bytes);
                using var bitmap = System.Drawing.Image.FromStream(stream);
                var metadata = new { method,
                    sourceBounds = sourceBounds is { } b ? new { x = b.X, y = b.Y, width = b.Width, height = b.Height } : null,
                    sourceWidth = bitmap.Width, sourceHeight = bitmap.Height,
                    width = frame?.Width ?? bitmap.Width, height = frame?.Height ?? bitmap.Height, frame,
                    windowDpi = targetHwnd == 0 ? (uint?)null : ReadDpi(targetHwnd),
                    screenFallback = method == "screen-fallback" };
                var result = BuildScreenshotResult(bytes, normalizedSavePath, overwrite, includeImage, frame);
                if (result.IsError != true) result.Content.Add(new McpContent { Text = JsonSerializer.Serialize(metadata, McpProtocol.JsonOptions) });
                return result;
            }

            if (background && (fullScreen || (string.IsNullOrEmpty(refId) && string.IsNullOrEmpty(handle))))
            {
                return Task.FromResult(ErrorResult("background capture requires a window handle or Window ref and cannot be combined with fullScreen"));
            }

            if (fullScreen)
            {
                // A full-screen capture would include windows of apps outside
                // the allowlist, so it is disabled while one is active.
                if (_processPolicy.IsRestricted)
                {
                    return Task.FromResult(ErrorResult(
                        "fullScreen capture is disabled while the app allowlist " +
                        $"({ProcessPolicy.EnvironmentVariable}) is active. Capture an allowed window by handle instead."));
                }
                capture = Capture.Screen();
                method = "screen";
            }
            else if (!string.IsNullOrEmpty(refId))
            {
                if (!_processPolicy.IsProcessAllowed(_elementRegistry.GetProcessIdForRef(refId)))
                    return Task.FromResult(ErrorResult(_processPolicy.DescribeDenied("Screenshot target")));
                var element = _elementRegistry.GetElement(refId);
                if (element == null)
                {
                    return Task.FromResult(ErrorResult($"Element not found: {refId}"));
                }

                // Element capture needs the UIA bounding rectangle, which hangs while
                // the app's provider is blocked; suggest window-handle capture instead
                // (its Win32 fallback works while blocked, and fullScreen may be
                // unavailable when an app allowlist is active).
                if (_invokeTracker.TryGetPending(_elementRegistry.GetProcessIdForRef(refId), out var pendingRef))
                {
                    return Task.FromResult(ErrorResult(PendingInvokeTracker.DescribeBlocked(pendingRef) +
                        " For screenshots, use a window handle instead of a ref.") with { Outcome = BlockedResult(pendingRef).Outcome });
                }

                if (background)
                {
                    if (element.ControlType != FlaUI.Core.Definitions.ControlType.Window)
                        return Task.FromResult(ErrorResult("background ref capture requires a Window element"));
                    if (!NativeWindowCapture.TryCaptureWindow(element.AsWindow(), out var image, out var reason))
                        return Task.FromResult(ErrorResult($"Native Window ref capture failed: {reason}"));
                    targetHwnd = element.Properties.NativeWindowHandle.ValueOrDefault;
                    sourceBounds = Win32Desktop.GetWindowBounds(targetHwnd);
                    method = "native-window";
                    return Task.FromResult(Finish(image));
                }
                sourceBounds = element.BoundingRectangle;
                targetHwnd = _sessionManager.GetWindowHwnd(_elementRegistry.WindowForRef(refId) ?? "");
                capture = Capture.Element(element);
            }
            else if (!string.IsNullOrEmpty(handle))
            {
                targetHwnd = _sessionManager.GetWindowHwnd(handle);
                if (!_processPolicy.IsProcessAllowed(_sessionManager.GetWindowProcessId(handle)))
                    return Task.FromResult(ErrorResult(_processPolicy.DescribeDenied("Screenshot target")));
                // While the app's UIA provider is blocked (pending pattern call, e.g. an
                // open modal dialog), fall back to a pure Win32 capture of the window
                // bounds so screenshots keep working.
                if (_invokeTracker.TryGetPending(_sessionManager.GetWindowProcessId(handle), out _))
                {
                    if (strictNative)
                        return Task.FromResult(ErrorResult("Native capture is unavailable while this provider is blocked. Screen-pixel fallback is disabled; no screenshot was taken."));
                    var hwnd = _sessionManager.GetWindowHwnd(handle);
                    var bounds = hwnd != 0 ? Win32Desktop.GetWindowBounds(hwnd) : null;
                    if (bounds == null)
                    {
                        return Task.FromResult(ErrorResult(
                            "This app's UI Automation provider is blocked and its window bounds are unknown. " +
                            "Use windows_list_windows to find the window (or the open dialog) and capture it " +
                            "by that handle instead."));
                    }
                    capture = Capture.Rectangle(bounds.Value);
                    sourceBounds = bounds;
                    method = "screen-fallback";
                }
                else
                {
                    // A provider can stop answering after a dialog opens even with no tracked
                    // call; the cached HWND paths below never touch UI Automation.
                    Window? window = null;
                    try
                    {
                        window = _sessionManager.GetWindow(handle);
                        if (window == null)
                        {
                            return Task.FromResult(ErrorResult($"Window not found: {handle}"));
                        }
                    }
                    catch (TimeoutException) { }

                    if (background)
                    {
                        if (NativeWindowCapture.TryCaptureHwnd(targetHwnd, out var backgroundImage, out var reason))
                        {
                            sourceBounds = Win32Desktop.GetWindowBounds(targetHwnd);
                            method = "native-window";
                            return Task.FromResult(Finish(backgroundImage));
                        }
                        if (strictNative)
                            return Task.FromResult(ErrorResult($"Native capture failed: {reason}. Screen-pixel fallback is disabled; no screenshot was taken."));
                    }

                    CaptureImage? elementCapture = null;
                    try
                    {
                        if (window != null)
                        {
                            sourceBounds = window.BoundingRectangle;
                            elementCapture = Capture.Element(window);
                            if (background) method = "screen-fallback";
                        }
                    }
                    catch (TimeoutException) { }
                    if (elementCapture == null)
                    {
                        var bounds = targetHwnd != 0 ? Win32Desktop.GetWindowBounds(targetHwnd) : null;
                        if (bounds == null)
                            return Task.FromResult(ErrorResult("UI Automation timed out for this window and its bounds are unknown. Use windows_list_windows for a current handle."));
                        elementCapture = Capture.Rectangle(bounds.Value);
                        sourceBounds = bounds;
                        method = "screen-fallback";
                    }
                    capture = elementCapture;
                }
            }
            else
            {
                // Resolve the foreground window through Win32 and authorize its process before
                // reading its provider; a focused-element walk can hang on unrelated apps.
                var foreground = Win32Desktop.GetForegroundWindow();
                if (foreground == 0)
                {
                    return Task.FromResult(ErrorResult("No foreground window found"));
                }

                Window? foregroundWindow;
                try
                {
                    foregroundWindow = CaptureForeground(Win32Desktop.GetProcessId(foreground), _processPolicy,
                        () => _sessionManager.Automation.FromHandle(foreground)?.AsWindow());
                }
                catch (UnauthorizedAccessException ex)
                {
                    return Task.FromResult(ErrorResult(ex.Message));
                }
                if (foregroundWindow == null)
                {
                    return Task.FromResult(ErrorResult("Could not read the foreground window"));
                }
                capture = Capture.Element(foregroundWindow);
                sourceBounds = foregroundWindow.BoundingRectangle;
                targetHwnd = foreground;
            }

            byte[] imageData;
            using (capture)
            {
                using var stream = new MemoryStream();
                capture.Bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                imageData = stream.ToArray();
            }

            return Task.FromResult(Finish(imageData));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ErrorResult($"Failed to capture screenshot: {ex.Message}"));
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);
    private static uint? ReadDpi(nint hwnd) => GetDpiForWindow(hwnd) is var dpi && dpi != 0 ? dpi : null;

    internal static T CaptureForeground<T>(int processId, ProcessPolicy policy, Func<T> capture)
    {
        if (!policy.IsProcessAllowed(processId))
        {
            var name = ProcessPolicy.TryGetProcessName(processId) ?? "unknown";
            throw new UnauthorizedAccessException(policy.DescribeDenied($"The foreground window's process '{name}'"));
        }
        return capture();
    }

    internal static bool TryNormalizeSavePath(string? savePath, bool overwrite, out string? normalizedPath, out string error)
    {
        normalizedPath = null;
        error = "";

        if (string.IsNullOrWhiteSpace(savePath))
        {
            return true;
        }

        if (!Path.IsPathFullyQualified(savePath))
        {
            error = $"savePath must be an absolute local path: {savePath}";
            return false;
        }

        if (savePath.StartsWith(@"\\") || savePath.StartsWith(@"\\?\") || savePath.StartsWith(@"\\.\"))
        {
            error = "savePath must be a local drive path; UNC and device paths are not allowed";
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(savePath);
        }
        catch (Exception ex)
        {
            error = $"savePath is invalid: {ex.Message}";
            return false;
        }

        if (!string.Equals(Path.GetExtension(fullPath), ".png", StringComparison.OrdinalIgnoreCase))
        {
            error = "savePath must end with .png";
            return false;
        }

        if (File.Exists(fullPath) && !overwrite)
        {
            error = $"savePath already exists; pass overwrite=true to replace it: {fullPath}";
            return false;
        }

        normalizedPath = fullPath;
        return true;
    }

    internal static McpToolResult BuildScreenshotResult(byte[] imageData, string? savePath, bool overwrite, bool includeImage, CaptureFrame? frame = null)
    {
        OperationContext.Check();
        if (frame != null) imageData = frame.Apply(imageData);
        OperationContext.Check();
        if (string.IsNullOrEmpty(savePath))
        {
            return ImageResult(imageData, "image/png");
        }

        try
        {
            var directory = Path.GetDirectoryName(savePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = Path.Combine(directory ?? Directory.GetCurrentDirectory(), $"{Path.GetFileName(savePath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                OperationContext.Check();
                File.WriteAllBytes(tempPath, imageData);
                OperationContext.Check();
                File.Move(tempPath, savePath, overwrite);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }
        catch (Exception ex)
        {
            return ErrorResult($"Failed to save screenshot to {savePath}: {ex.Message}");
        }

        if (!includeImage) return frame == null
            ? TextResult(JsonSerializer.Serialize(new { path = savePath, bytes = imageData.Length }))
            : TextResult(JsonSerializer.Serialize(new { path = savePath, bytes = imageData.Length, frame }));
        return new McpToolResult
        {
            Content = new List<McpContent>
            {
                new() { Type = "text", Text = $"Screenshot saved to {savePath}" },
                new() { Type = "image", Data = Convert.ToBase64String(imageData), MimeType = "image/png" }
            }
        };
    }
}
