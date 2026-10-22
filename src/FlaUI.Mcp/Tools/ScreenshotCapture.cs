using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Mcp.Core;

namespace FlaUI.Mcp.Tools;

/// <summary>Step 3 of windows_screenshot: read the pixels, by the path the target allows.</summary>
public partial class ScreenshotTool
{
    /// <summary>Capture and finish, or the refusal or native result a capture path returned early.</summary>
    private McpToolResult CaptureTarget(ShotRequest request, Shot shot)
    {
        if (
            request.Background
            && (request.FullScreen || (string.IsNullOrEmpty(request.RefId) && string.IsNullOrEmpty(request.Handle)))
        )
        {
            return ErrorResult(
                "background capture requires a window handle or Window ref and cannot be combined with fullScreen"
            );
        }

        CaptureImage? capture;
        McpToolResult? early;
        if (request.FullScreen)
        {
            early = CaptureScreen(shot, out capture);
        }
        else if (!string.IsNullOrEmpty(request.RefId))
        {
            early = CaptureRef(request, shot, out capture);
        }
        else if (!string.IsNullOrEmpty(request.Handle))
        {
            early = CaptureHandle(request, shot, out capture);
        }
        else
        {
            early = CaptureForegroundWindow(shot, out capture);
        }

        if (early != null)
        {
            return early;
        }

        byte[] imageData;
        using (capture)
        {
            using var stream = new MemoryStream();
            capture!.Bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            imageData = stream.ToArray();
        }

        return Finish(request, shot, imageData);
    }

    private McpToolResult? CaptureScreen(Shot shot, out CaptureImage? capture)
    {
        capture = null;
        // A full-screen capture would include windows of apps outside
        // the allowlist, so it is disabled while one is active.
        if (_processPolicy.IsRestricted)
        {
            return ErrorResult(
                "fullScreen capture is disabled while the app allowlist "
                    + $"({ProcessPolicy.EnvironmentVariable}) is active. Capture an allowed window by handle instead."
            );
        }

        capture = Capture.Screen();
        shot.Method = "screen";
        return null;
    }

    private McpToolResult? CaptureRef(ShotRequest request, Shot shot, out CaptureImage? capture)
    {
        capture = null;
        var refId = request.RefId!;
        if (!_processPolicy.IsProcessAllowed(_elementRegistry.GetProcessIdForRef(refId)))
        {
            return ErrorResult(_processPolicy.DescribeDenied("Screenshot target"));
        }

        var element = _elementRegistry.GetElement(refId);
        if (element == null)
        {
            return ErrorResult($"Element not found: {refId}");
        }

        // Element capture needs the UIA bounding rectangle, which hangs while
        // the app's provider is blocked; suggest window-handle capture instead
        // (its Win32 fallback works while blocked, and fullScreen may be
        // unavailable when an app allowlist is active).
        if (_invokeTracker.TryGetPending(_elementRegistry.GetProcessIdForRef(refId), out var pendingRef))
        {
            return ErrorResult(
                PendingInvokeTracker.DescribeBlocked(pendingRef)
                    + " For screenshots, use a window handle instead of a ref."
            ) with
            {
                Outcome = BlockedResult(pendingRef).Outcome,
            };
        }

        if (request.Background)
        {
            if (element.ControlType != FlaUI.Core.Definitions.ControlType.Window)
            {
                return ErrorResult("background ref capture requires a Window element");
            }

            if (!NativeWindowCapture.TryCaptureWindow(element.AsWindow(), out var image, out var reason))
            {
                return ErrorResult($"Native Window ref capture failed: {reason}");
            }

            shot.TargetHwnd = element.Properties.NativeWindowHandle.ValueOrDefault;
            shot.SourceBounds = InputDesktop.Current.WindowBounds(shot.TargetHwnd);
            shot.Method = "native-window";
            return Finish(request, shot, image);
        }

        shot.SourceBounds = element.BoundingRectangle;
        shot.TargetHwnd = _sessionManager.GetWindowHwnd(_elementRegistry.WindowForRef(refId) ?? "");
        capture = Capture.Element(element);
        return null;
    }

    private McpToolResult? CaptureHandle(ShotRequest request, Shot shot, out CaptureImage? capture)
    {
        capture = null;
        var handle = request.Handle!;
        shot.TargetHwnd = _sessionManager.GetWindowHwnd(handle);
        if (WindowIsGone(shot.TargetHwnd))
        {
            return ErrorResult(ClosedWindowMessage(handle));
        }

        if (!_processPolicy.IsProcessAllowed(_sessionManager.GetWindowProcessId(handle)))
        {
            return ErrorResult(_processPolicy.DescribeDenied("Screenshot target"));
        }
        // While the app's UIA provider is blocked (pending pattern call, e.g. an
        // open modal dialog), fall back to a pure Win32 capture of the window
        // bounds so screenshots keep working.
        if (_invokeTracker.TryGetPending(_sessionManager.GetWindowProcessId(handle), out _))
        {
            return CaptureBlockedHandle(request, shot, out capture);
        }

        return CaptureLiveHandle(request, shot, out capture);
    }

    private McpToolResult? CaptureBlockedHandle(ShotRequest request, Shot shot, out CaptureImage? capture)
    {
        capture = null;
        if (request.StrictNative)
        {
            return ErrorResult(
                "Native capture is unavailable while this provider is blocked. Screen-pixel fallback is disabled; no screenshot was taken."
            );
        }

        var hwnd = _sessionManager.GetWindowHwnd(request.Handle!);
        var bounds = hwnd != 0 ? InputDesktop.Current.WindowBounds(hwnd) : null;
        if (bounds == null)
        {
            return ErrorResult(
                "This app's UI Automation provider is blocked and its window bounds are unknown. "
                    + "Use windows_list_windows to find the window (or the open dialog) and capture it "
                    + "by that handle instead."
            );
        }

        capture = Capture.Rectangle(bounds.Value);
        shot.SourceBounds = bounds;
        shot.Method = "screen-fallback";
        // The provider is blocked because a modal is up, so the modal is in front of the
        // target by construction and strictNative was already refused above. Refusing for
        // occlusion here would leave this path with no way to capture anything.
        shot.OcclusionExempt = true;
        return null;
    }

    private McpToolResult? CaptureLiveHandle(ShotRequest request, Shot shot, out CaptureImage? capture)
    {
        capture = null;
        var handle = request.Handle!;
        // A provider can stop answering after a dialog opens even with no tracked
        // call; the cached HWND paths below never touch UI Automation.
        Window? window = null;
        try
        {
            window = _sessionManager.GetWindow(handle);
            if (window == null)
            {
                return ErrorResult($"Window not found: {handle}");
            }
        }
        catch (TimeoutException) { }

        if (request.Background)
        {
            if (NativeWindowCapture.TryCaptureHwnd(shot.TargetHwnd, out var backgroundImage, out var reason))
            {
                shot.SourceBounds = InputDesktop.Current.WindowBounds(shot.TargetHwnd);
                shot.Method = "native-window";
                return Finish(request, shot, backgroundImage);
            }

            if (request.StrictNative)
            {
                return ErrorResult(
                    $"Native capture failed: {reason}. Screen-pixel fallback is disabled; no screenshot was taken."
                );
            }
        }

        return CaptureWindowPixels(request, shot, window, out capture);
    }

    private McpToolResult? CaptureWindowPixels(
        ShotRequest request,
        Shot shot,
        Window? window,
        out CaptureImage? capture
    )
    {
        CaptureImage? elementCapture = null;
        try
        {
            if (window != null)
            {
                shot.SourceBounds = window.BoundingRectangle;
                elementCapture = Capture.Element(window);
                if (request.Background)
                {
                    shot.Method = "screen-fallback";
                }
            }
        }
        catch (TimeoutException) { }

        if (elementCapture == null)
        {
            var bounds = shot.TargetHwnd != 0 ? InputDesktop.Current.WindowBounds(shot.TargetHwnd) : null;
            if (bounds == null)
            {
                capture = null;
                return ErrorResult(
                    "UI Automation timed out for this window and its bounds are unknown. Use windows_list_windows for a current handle."
                );
            }

            elementCapture = Capture.Rectangle(bounds.Value);
            shot.SourceBounds = bounds;
            shot.Method = "screen-fallback";
        }

        capture = elementCapture;
        return null;
    }

    private McpToolResult? CaptureForegroundWindow(Shot shot, out CaptureImage? capture)
    {
        capture = null;
        // Resolve the foreground window through Win32 and authorize its process before
        // reading its provider; a focused-element walk can hang on unrelated apps.
        var foreground = InputDesktop.Current.Foreground();
        if (foreground == 0)
        {
            return ErrorResult("No foreground window found");
        }

        Window? foregroundWindow;
        try
        {
            foregroundWindow = ScreenshotChecks.CaptureForeground(
                InputDesktop.Current.ProcessOf(foreground),
                _processPolicy,
                () => _sessionManager.Automation.FromHandle(foreground)?.AsWindow()
            );
        }
        catch (UnauthorizedAccessException ex)
        {
            return ErrorResult(ex.Message);
        }

        if (foregroundWindow == null)
        {
            return ErrorResult("Could not read the foreground window");
        }

        capture = Capture.Element(foregroundWindow);
        shot.SourceBounds = foregroundWindow.BoundingRectangle;
        shot.TargetHwnd = foreground;
        return null;
    }
}
