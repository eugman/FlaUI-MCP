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
        "Capture a window or element as PNG. Use savePath with includeImage=false to save an artifact without returning image tokens.";

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
                description = "Element ref to capture. If omitted, captures the whole window."
            },
            fullScreen = new
            {
                type = "boolean",
                description = "Capture the entire screen (default: false)"
            },
            background = new
            {
                type = "boolean",
                description = "Use native whole-window capture. Window handles retain normal-capture fallback; Window refs require native capture to succeed (no element-crop fallback). Cannot combine with fullScreen."
            },
            strictNative = new
            {
                type = "boolean",
                description = "Opt in to native window capture without screen-pixel fallback. Implies background=true; use an explicit window handle or Window ref."
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
            }
        }
    };

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var handle = GetStringArgument(arguments, "handle");
        var refId = GetStringArgument(arguments, "ref");
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
                    return Task.FromResult(ErrorResult(
                        PendingInvokeTracker.DescribeBlocked(pendingRef) +
                        " For screenshots, use a window handle instead of a ref."));
                }

                if (background)
                {
                    if (element.ControlType != FlaUI.Core.Definitions.ControlType.Window)
                        return Task.FromResult(ErrorResult("background ref capture requires a Window element"));
                    if (!NativeWindowCapture.TryCaptureWindow(element.AsWindow(), out var image, out var reason))
                        return Task.FromResult(ErrorResult($"Native Window ref capture failed: {reason}"));
                    return Task.FromResult(BuildScreenshotResult(image, normalizedSavePath, overwrite, includeImage, frame));
                }
                capture = Capture.Element(element);
            }
            else if (!string.IsNullOrEmpty(handle))
            {
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
                }
                else
                {
                    var window = _sessionManager.GetWindow(handle);
                    if (window == null)
                    {
                        return Task.FromResult(ErrorResult($"Window not found: {handle}"));
                    }

                    if (background)
                    {
                        if (NativeWindowCapture.TryCaptureWindow(window, out var backgroundImage, out var reason))
                            return Task.FromResult(BuildScreenshotResult(backgroundImage, normalizedSavePath, overwrite, includeImage, frame));
                        if (strictNative)
                            return Task.FromResult(ErrorResult($"Native capture failed: {reason}. Screen-pixel fallback is disabled; no screenshot was taken."));
                    }

                    capture = Capture.Element(window);
                }
            }
            else
            {
                // Capturing the foreground window: verify it belongs to an
                // allowed app before touching it.
                if (_processPolicy.IsRestricted)
                {
                    var foregroundPid = Win32Desktop.GetForegroundWindowProcessId();
                    if (!_processPolicy.IsProcessAllowed(foregroundPid))
                    {
                        var name = ProcessPolicy.TryGetProcessName(foregroundPid) ?? "unknown";
                        return Task.FromResult(ErrorResult(
                            _processPolicy.DescribeDenied($"The foreground window's process '{name}'")));
                    }
                }

                // Capture foreground window
                var focusedElement = _sessionManager.Automation.FocusedElement();
                if (focusedElement == null)
                {
                    return Task.FromResult(ErrorResult("No focused window found"));
                }

                // Walk up to find the window
                var current = focusedElement;
                while (current != null && current.Properties.ControlType.ValueOrDefault != FlaUI.Core.Definitions.ControlType.Window)
                {
                    current = current.Parent;
                }

                if (current == null)
                {
                    return Task.FromResult(ErrorResult("Could not find window for focused element"));
                }

                capture = Capture.Element(current);
            }

            byte[] imageData;
            using (capture)
            {
                using var stream = new MemoryStream();
                capture.Bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                imageData = stream.ToArray();
            }

            return Task.FromResult(BuildScreenshotResult(imageData, normalizedSavePath, overwrite, includeImage, frame));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ErrorResult($"Failed to capture screenshot: {ex.Message}"));
        }
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
