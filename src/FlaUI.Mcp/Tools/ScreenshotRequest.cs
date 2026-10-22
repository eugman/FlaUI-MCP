using System.Text.Json;
using FlaUI.Core.Capturing;
using FlaUI.Mcp.Core;

namespace FlaUI.Mcp.Tools;

/// <summary>What one windows_screenshot call asks for, read and checked before any pixel is read.</summary>
internal sealed record ShotRequest(
    string? Handle,
    string? RefId,
    bool FullScreen,
    bool Background,
    bool StrictNative,
    string? NormalizedSavePath,
    bool Overwrite,
    bool IncludeImage,
    CaptureFrame? Frame,
    string[] LocateRefs,
    bool ParkPointer,
    bool AllowOccludedPixels,
    bool IncludeMetadata
);

/// <summary>What the capture found out along the way: how it read the pixels, from where, and the park.</summary>
internal sealed class Shot
{
    public string Method { get; set; } = "screen-uia-bounds";

    public System.Drawing.Rectangle? SourceBounds { get; set; }

    public nint TargetHwnd { get; set; }

    // Set only where a modal is in front of the target by construction, so the occlusion
    // guard would otherwise leave that path with no way to capture at all.
    public bool OcclusionExempt { get; set; }

    public string? ParkWarning { get; set; }

    public Proof? ParkProof { get; set; }
}

/// <summary>Step 1 of windows_screenshot: resolve and check the request.</summary>
public partial class ScreenshotTool
{
    /// <summary>The request, or the refusal for a malformed or disallowed one; nothing is captured yet.</summary>
    private McpToolResult? ReadRequest(JsonElement? arguments, out ShotRequest request)
    {
        request = null!;
        var handle = GetStringArgument(arguments, "handle");
        var refId = GetStringArgument(arguments, "ref");
        if (CheckRef(refId, handle) is { } refError)
        {
            return refError;
        }

        var fullScreen = GetBoolArgument(arguments, "fullScreen", false);
        var background = GetBoolArgument(arguments, "background", false);
        var strictNative = GetBoolArgument(arguments, "strictNative", false);
        background |= strictNative;
        var savePath = GetStringArgument(arguments, "savePath");
        var overwrite = GetBoolArgument(arguments, "overwrite", false);
        var includeImage = GetBoolArgument(arguments, "includeImage", true);
        var frame =
            arguments is { } a && a.TryGetProperty("frame", out var f) && f.ValueKind != JsonValueKind.Null
                ? f.Deserialize<CaptureFrame>()
                : null;
        frame?.Validate();
        if (!includeImage && string.IsNullOrWhiteSpace(savePath))
        {
            return ErrorResult("includeImage=false requires savePath");
        }

        var locateRefs = GetArgument<string[]>(arguments, "locate") ?? [];
        if (CheckLocate(locateRefs, fullScreen) is { } locateError)
        {
            return locateError;
        }

        if (!ScreenshotChecks.TryNormalizeSavePath(savePath, overwrite, out var normalizedSavePath, out var pathError))
        {
            return ErrorResult(pathError);
        }

        request = new ShotRequest(
            handle,
            refId,
            fullScreen,
            background,
            strictNative,
            normalizedSavePath,
            overwrite,
            includeImage,
            frame,
            locateRefs,
            GetBoolArgument(arguments, "parkPointer", false),
            GetBoolArgument(arguments, "allowOccludedPixels"),
            GetBoolArgument(arguments, "includeMetadata")
        );
        return null;
    }

    private McpToolResult? CheckRef(string? refId, string? handle)
    {
        if (refId == null)
        {
            return null;
        }

        try
        {
            _elementRegistry.ResolveRef(refId, handle);
            return null;
        }
        catch (Exception ex)
            when (ex
                    is ArgumentException
                        or InvalidOperationException
                        or AutomationRefusal
                        or System.ComponentModel.Win32Exception
            )
        {
            return ErrorResult(ex.Message);
        }
    }

    // Everything about locate that can be known before a pixel is read is checked here, so a
    // malformed call does not perform a full capture and PNG encode only to throw it away.
    private McpToolResult? CheckLocate(string[] locateRefs, bool fullScreen)
    {
        if (locateRefs.Length == 0)
        {
            return null;
        }

        if (fullScreen)
        {
            return ErrorResult(
                "locate needs a known capture origin, which fullScreen does not record. "
                    + "Capture the window by handle or ref instead. No screenshot was taken."
            );
        }

        foreach (var reference in locateRefs.Distinct())
        {
            if (!_elementRegistry.HasElement(reference))
            {
                return ErrorResult($"locate ref {reference} is not a known element ref. No screenshot was taken.");
            }

            var refProcess = _elementRegistry.GetProcessIdForRef(reference);
            if (!_processPolicy.IsProcessAllowed(refProcess))
            {
                return ErrorResult(_processPolicy.DescribeDenied($"locate ref {reference}"));
            }
            // BoundingRectangle is a UIA read, which is exactly what hangs while a provider is
            // blocked — the state the screen-pixel fallback exists to survive.
            if (_invokeTracker.TryGetPending(refProcess, out var pendingRef))
            {
                return ErrorResult(
                    $"locate cannot measure {reference} while this app's UI Automation provider is blocked. "
                        + PendingInvokeTracker.DescribeBlocked(pendingRef)
                );
            }
        }

        return null;
    }
}
